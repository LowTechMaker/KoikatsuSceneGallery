using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The row of active filters under the search box, each removable on its own,
/// and the resolution switch inside the filter flyout.
/// </summary>
/// <remarks>
/// <para>The filter button's badge said how many filters were on but not which,
/// so a grid that looked short had to be explained by opening the flyout and
/// reading every box. The row says it on the page.</para>
/// <para>The scopes follow <see cref="Clear_Click"/>: inside a group or an
/// author's or tag's browser, only the search and the metadata filters belong
/// to this browser - the rating, game and resolution filters are the whole
/// library's and are not cleared from a corner of it. The platform has no chip:
/// the title bar already names it, and it switches the whole app.</para>
/// </remarks>
public sealed partial class LibraryBrowser
{
    private sealed record ActiveFilter(string Label, Action Clear);

    private List<ActiveFilter> ActiveFilters()
    {
        var list = new List<ActiveFilter>();
        var query = SearchBox.Text;
        if (!string.IsNullOrWhiteSpace(query))
            list.Add(new(UiText.Format("Chip_Search", query.Trim()), () => { SetSearchText(""); ApplySearch(); }));

        var libraryWide = _scope is null && _groupKey is null;
        if (libraryWide && VM is GalleryViewModel scene)
        {
            if (scene.RatingFilter != RatingFilterOption.All)
                list.Add(new($"{UiText.Get("SceneGallery_Rating.Header")}：{RatingFilterLabels()[(int)scene.RatingFilter]}",
                    () => scene.RatingFilter = RatingFilterOption.All));
            if (scene.GameFilter != GameFilterOption.All)
                list.Add(new($"{UiText.Get("SceneGallery_Environment.Header")}：{GameLabels()[(int)scene.GameFilter]}",
                    () => scene.GameFilter = GameFilterOption.All));
        }

        if (libraryWide && VM is CharacterGalleryViewModel character && character.SourceFilter != CardSourceFilterOption.All)
            list.Add(new($"{UiText.Get("Browser_Source")}：{SourceLabels()[(int)character.SourceFilter]}",
                () => character.SourceFilter = CardSourceFilterOption.All));

        if (_groupKey is null && GetMetadataFilters() is { } metadata)
        {
            if (metadata.Sex is not null && _metadataSex?.SelectedItem is { } sex)
                list.Add(new($"{UiText.Get("Metadata_Sex")}：{sex}", () => metadata.Sex = null));
            if (metadata.Personality is not null && _metadataPersonality?.SelectedItem is { } personality)
                list.Add(new($"{UiText.Get("Metadata_Personality")}：{personality}", () => metadata.Personality = null));
            if (metadata.PluginGuid is { } guid)
                list.Add(new($"{UiText.Get("Metadata_PluginGuid")}：{guid}", () => metadata.PluginGuid = null));
        }

        if (libraryWide && ResolutionFilter() is { Enabled: true, Count: > 0 } resolution)
            list.Add(new(UiText.Format("Chip_Resolution", resolution.Count), () => SetResolutionFilter(false)));

        return list;
    }

    private static string[] GameLabels() =>
        [UiText.Get("Gallery_FilterAll.Content"), "Koikatsu", "Koikatsu Sunshine", UiText.Get("Gallery_EnvUnknown.Content")];

    private static string[] SourceLabels() =>
        [UiText.Get("Gallery_FilterAll.Content"), "Koikatsu Sunshine", "Koikatsu HF", "Madevil", UiText.Get("Gallery_EnvUnknown.Content")];

    /// <summary>This library's resolution filter: whether it is on, and how many sizes the list allows.</summary>
    private (bool Enabled, int Count)? ResolutionFilter() => VM switch
    {
        GalleryViewModel => (_settings.ResolutionFilterEnabled, _settings.AllowedResolutions.Count),
        CharacterGalleryViewModel => (_settings.CharacterResolutionFilterEnabled, _settings.CharacterAllowedResolutions.Count),
        CoordinateGalleryViewModel => (_settings.CoordinateResolutionFilterEnabled, _settings.CoordinateAllowedResolutions.Count),
        _ => null,
    };

    private void SetResolutionFilter(bool enabled)
    {
        switch (VM)
        {
            case GalleryViewModel: _settings.ResolutionFilterEnabled = enabled; break;
            case CharacterGalleryViewModel: _settings.CharacterResolutionFilterEnabled = enabled; break;
            case CoordinateGalleryViewModel: _settings.CoordinateResolutionFilterEnabled = enabled; break;
        }

        QueueRefresh();
    }

    /// <summary>Rebuilds the chip row and syncs the flyout's resolution switch. Called from Refresh.</summary>
    private void UpdateFilterChips()
    {
        var filters = ActiveFilters();
        FilterChips.Children.Clear();
        foreach (var filter in filters)
        {
            var chip = new Button
            {
                Padding = new Thickness(10, 3, 8, 3),
                CornerRadius = new CornerRadius(12),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = filter.Label,
                            MaxWidth = 280,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                            VerticalAlignment = VerticalAlignment.Center,
                        },
                        new FontIcon { Glyph = "", FontSize = 10, VerticalAlignment = VerticalAlignment.Center },
                    },
                },
            };
            AutomationProperties.SetName(chip, UiText.Format("Chip_RemoveName", filter.Label));
            ToolTipService.SetToolTip(chip, UiText.Format("Chip_RemoveName", filter.Label));
            var clear = filter.Clear;
            chip.Click += (_, _) => { clear(); QueueRefresh(); SearchBox.Focus(FocusState.Programmatic); };
            FilterChips.Children.Add(chip);
        }

        if (filters.Count > 1)
        {
            var all = new HyperlinkButton { Content = UiText.Get("Chips_ClearAll"), Padding = new Thickness(6, 3, 6, 3) };
            all.Click += (_, _) =>
            {
                foreach (var filter in ActiveFilters()) filter.Clear();
                QueueRefresh();
            };
            FilterChips.Children.Add(all);
        }

        FilterChips.Visibility = filters.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (ResolutionFilter() is { } resolution && _scope is null && _groupKey is null)
        {
            ResolutionToggle.Visibility = Visibility.Visible;
            _syncing = true;
            ResolutionToggle.IsOn = resolution.Enabled;
            _syncing = false;
            // An empty list filters nothing; the switch would promise more than it does.
            ResolutionToggle.IsEnabled = resolution.Count > 0;
            ResolutionFilterStatus.Visibility = Visibility.Visible;
            ResolutionFilterStatus.Text = resolution.Count > 0
                ? UiText.Format("Browser_ResolutionListCount", resolution.Count)
                : UiText.Get("Browser_ResolutionListEmpty");
        }
        else
        {
            ResolutionToggle.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowHiddenToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_adapter is not null) VM.ShowHiddenCards = ShowHiddenToggle.IsOn;
    }

    private void ResolutionToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_syncing || ResolutionFilter() is not { } resolution || resolution.Enabled == ResolutionToggle.IsOn) return;
        SetResolutionFilter(ResolutionToggle.IsOn);
    }
}

using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The library at a glance, beside the featured card: how many scenes, how
/// many came in this week, how many still have no rating of the user's own,
/// and what the import is doing. Each line opens where it leads.
/// </summary>
/// <remarks>
/// The dashboard half of the home page (user's pick: a featured card plus a
/// dashboard). Counts are of the scene library as scanned; the unrated count
/// needs the user's ratings, which are read off the UI thread on first use.
/// </remarks>
internal sealed class HomeStats : UserControl
{
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    private readonly UserCardRatingStore _ratings = App.Services.GetRequiredService<UserCardRatingStore>();
    private readonly ImportViewModel? _import = App.Services.GetService<ImportViewModel>();
    private readonly TextBlock _scenes = new() { Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] };
    private readonly Button _new;
    private readonly Button _unrated;
    private readonly Button _importLine;
    private readonly ProgressRing _importRing = new() { Width = UiMetrics.RingSize, Height = UiMetrics.RingSize, IsActive = false, Visibility = Visibility.Collapsed };
    private bool _counting;

    /// <summary>"New this week" was clicked: those cards, newest first.</summary>
    public event Action<IReadOnlyList<Models.CardBase>>? NewRequested;

    public HomeStats()
    {
        AutomationProperties.SetAutomationId(this, "HomeStats");
        _new = Line(Glyphs.Add, "HomeStatsNew", () => NewRequested?.Invoke(NewCards()));
        _unrated = Line(Glyphs.Rating, "HomeStatsUnrated", () => MainWindow.Instance?.NavigateToSection("rating"));
        _importLine = Line(Glyphs.Import, "HomeStatsImport", () => MainWindow.Instance?.NavigateToSection("import"));

        var heading = new TextBlock
        {
            Text = UiText.Get("Home_Stats"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        };
        var importRow = new Grid { ColumnSpacing = 8 };
        importRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        importRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        importRow.Children.Add(_importLine);
        Grid.SetColumn(_importRing, 1);
        importRow.Children.Add(_importRing);

        var layout = new StackPanel { Spacing = 4 };
        layout.Children.Add(heading);
        layout.Children.Add(_scenes);
        layout.Children.Add(new Border { Height = 8 });
        layout.Children.Add(_new);
        layout.Children.Add(_unrated);
        layout.Children.Add(importRow);

        Content = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = layout,
        };
        if (_import is not null) _import.PropertyChanged += Import_Changed;
        _ratings.RatingChanged += _ => DispatcherQueue.TryEnqueue(Refresh);
        ShowImport();
    }

    /// <summary>Counts again (the page calls this when the library or the ratings change).</summary>
    public void Refresh()
    {
        var cards = _gallery.Cards;
        _scenes.Text = UiText.Format("Home_StatsScenes", cards.Count.ToString("N0"));
        SetLine(_new, UiText.Format("Home_StatsNew", NewCards().Count.ToString("N0")));
        if (_counting) return;
        _counting = true;
        var roots = _settings.FolderPaths.ToArray();
        var paths = cards.Select(card => card.FilePath).ToArray();
        _ = CountUnratedAsync(roots, paths);
    }

    private async Task CountUnratedAsync(string[] roots, string[] paths)
    {
        try
        {
            var unrated = await Task.Run(() =>
            {
                _ratings.EnsureLoaded(roots);
                return paths.Count(path => _ratings.TryGetByPath(path) is null);
            });
            SetLine(_unrated, UiText.Format("Home_StatsUnrated", unrated.ToString("N0")));
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<IAppLogger>().LogError("Home.CountUnrated", ex);
        }
        finally { _counting = false; }
    }

    private IReadOnlyList<Models.CardBase> NewCards()
    {
        var since = DateTime.Now.AddDays(-UiMetrics.HomeNewDays);
        return [.. _gallery.Cards.Where(card => card.DateCreated >= since).OrderByDescending(card => card.DateCreated)];
    }

    private void Import_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImportViewModel.IsImporting) or nameof(ImportViewModel.IsAnalyzing) or nameof(ImportViewModel.AnalysisPendingCount))
            DispatcherQueue.TryEnqueue(ShowImport);
    }

    private void ShowImport()
    {
        var busy = _import is { IsImporting: true } or { IsAnalyzing: true };
        var text = _import switch
        {
            null => UiText.Get("Home_StatsImportIdle"),
            { IsImporting: true } => UiText.Get("Home_StatsImporting"),
            { IsAnalyzing: true } i => UiText.Format("Home_StatsAnalyzing", i.AnalysisPendingCount),
            _ => UiText.Get("Home_StatsImportIdle"),
        };
        SetLine(_importLine, text);
        _importRing.IsActive = busy;
        _importRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A line that is a link: an icon, the text, and a chevron when it leads somewhere.</summary>
    private static Button Line(string glyph, string automationId, Action click)
    {
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 4, 6, 4),
        };
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = UiMetrics.IconSize, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        var text = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var chevron = new FontIcon { Glyph = Glyphs.Next, FontSize = 10, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] };
        Grid.SetColumn(chevron, 2);
        row.Children.Add(chevron);
        button.Content = row;
        button.Tag = text;
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => click();
        return button;
    }

    private static void SetLine(Button line, string text)
    {
        if (line.Tag is TextBlock block) block.Text = text;
        AutomationProperties.SetName(line, text);
    }
}

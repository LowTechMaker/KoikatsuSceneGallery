using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The details pane of the authors page (wave 17), the counterpart of the
/// cards' <see cref="CardInspector"/>: a click on an author selects it and this
/// says who it is, how many cards of each kind there are and when they last
/// changed, shows a few of them, and offers opening the author's page, their
/// profile online, or refreshing them. A double click (or Enter) opens the page.
/// </summary>
internal sealed class AuthorInspector : UserControl
{
    private const int PreviewCount = UiMetrics.AuthorPanePreviews;

    private readonly StackPanel _body = new() { Spacing = 16, Padding = new Thickness(UiMetrics.PanePadding) };
    private readonly TextBlock _empty = Caption(UiText.Get("AuthorInspector_Empty"));
    private readonly StackPanel _one = new() { Spacing = 16 };
    private readonly PersonPicture _avatar = new() { Width = UiMetrics.AuthorAvatarSize, Height = UiMetrics.AuthorAvatarSize, VerticalAlignment = VerticalAlignment.Top };
    private readonly TextBlock _name = new()
    {
        Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };
    private readonly TextBlock _counts = Caption("");
    private readonly TextBlock _updated = Caption("");
    // The cards' pane's layout (user request: the two panes should match): small
    // icon buttons on the left, the one accent "Open" on the right; it used to be
    // three full-width buttons stacked, the loudest thing in the pane.
    private readonly Button _open = new()
    {
        Content = UiText.Get("Inspector_Open"),
        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
    };
    private readonly Button _profile = IconButton("\uE774", "AuthorInspector_Profile");
    private readonly Button _refresh = IconButton("\uE72C", "AuthorInspector_Refresh");
    /// <summary>Recent cards, in equal columns that fill the pane (a wrap of fixed squares left a gap on the right).</summary>
    private readonly Grid _previews = new() { ColumnSpacing = UiMetrics.AuthorPanePreviewGap, RowSpacing = UiMetrics.AuthorPanePreviewGap };

    private AuthorSummary? _summary;

    /// <summary>The user asked to open the author's page.</summary>
    public event Action<AuthorSummary>? OpenRequested;

    /// <summary>The user asked to refresh the author from their platform.</summary>
    public event Action<AuthorSummary>? RefreshRequested;

    public AuthorInspector()
    {
        AutomationProperties.SetAutomationId(this, "AuthorInspector");
        AutomationProperties.SetName(this, UiText.Get("Inspector_Name"));
        Content = new Border
        {
            CornerRadius = (CornerRadius)Application.Current.Resources["OverlayCornerRadius"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };

        AutomationProperties.SetAutomationId(_name, "AuthorInspector_Name");
        AutomationProperties.SetAutomationId(_open, "AuthorInspector_Open");
        AutomationProperties.SetAutomationId(_profile, "AuthorInspector_Profile");
        AutomationProperties.SetAutomationId(_refresh, "AuthorInspector_Refresh");
        var top = new Grid { ColumnSpacing = 12 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.Children.Add(_avatar);
        var heading = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(_name);
        heading.Children.Add(_counts);
        heading.Children.Add(_updated);
        Grid.SetColumn(heading, 1);
        top.Children.Add(heading);
        _one.Children.Add(top);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(_profile);
        tools.Children.Add(_refresh);
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(tools);
        Grid.SetColumn(_open, 1);
        row.Children.Add(_open);
        _one.Children.Add(row);

        for (var c = 0; c < UiMetrics.AuthorPanePreviewColumns; c++)
            _previews.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Square tiles: as tall as a column is wide, whatever the pane's width.
        _previews.SizeChanged += (_, _) => SizePreviews();
        _one.Children.Add(_previews);

        _open.Click += (_, _) => { if (_summary is { } summary) OpenRequested?.Invoke(summary); };
        _refresh.Click += (_, _) => { if (_summary is { } summary) RefreshRequested?.Invoke(summary); };
        _profile.Click += (_, _) => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorInspector.Profile", async () =>
        {
            if (_summary?.Display is { HasProfileUrl: true } display)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(display.ProfileUrl));
        });

        _body.Children.Add(_empty);
        _body.Children.Add(_one);
        Show(null);
    }

    /// <summary>Shows an author, or the hint when none is selected.</summary>
    public void Show(AuthorSummary? summary)
    {
        _summary = summary;
        _empty.Visibility = summary is null ? Visibility.Visible : Visibility.Collapsed;
        _one.Visibility = summary is null ? Visibility.Collapsed : Visibility.Visible;
        if (summary is null) return;

        var display = summary.Display;
        _avatar.DisplayName = display.Name;
        _avatar.ProfilePicture = display.AvatarSource;
        _name.Text = display.Name;
        _counts.Text = UiText.Format("AuthorInspector_Counts", summary.SceneCount, summary.CharacterCount, summary.CoordinateCount);
        _updated.Text = summary.LastUpdated == default ? "" : UiText.Format("AuthorInspector_Updated", summary.LastUpdated.ToString("d"));
        _updated.Visibility = summary.LastUpdated == default ? Visibility.Collapsed : Visibility.Visible;
        _profile.Visibility = display.HasProfileUrl ? Visibility.Visible : Visibility.Collapsed;
        // A local source has nothing online to refresh from.
        _refresh.Visibility = display.IsLocalSource ? Visibility.Collapsed : Visibility.Visible;

        _previews.Children.Clear();
        _previews.RowDefinitions.Clear();
        var index = 0;
        foreach (var path in summary.ThumbnailPaths.Take(PreviewCount))
        {
            var columns = UiMetrics.AuthorPanePreviewColumns;
            if (index % columns == 0) _previews.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var tile = new Border
            {
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage { DecodePixelWidth = 240, UriSource = new Uri(path) },
                },
            };
            Grid.SetRow(tile, index / columns);
            Grid.SetColumn(tile, index % columns);
            _previews.Children.Add(tile);
            index++;
        }
        SizePreviews();
    }

    private void SizePreviews()
    {
        var columns = UiMetrics.AuthorPanePreviewColumns;
        var width = _previews.ActualWidth;
        if (width <= 0) return;
        var side = Math.Floor((width - UiMetrics.AuthorPanePreviewGap * (columns - 1)) / columns);
        foreach (var child in _previews.Children)
        {
            if (child is FrameworkElement tile && Math.Abs(tile.Height - side) > 0.5) tile.Height = side;
        }
    }

    private static Button IconButton(string glyph, string tipKey)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = UiMetrics.IconSize },
            Padding = new Thickness(8, 6, 8, 6),
        };
        ToolTipService.SetToolTip(button, UiText.Get(tipKey));
        AutomationProperties.SetName(button, UiText.Get(tipKey));
        return button;
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
}

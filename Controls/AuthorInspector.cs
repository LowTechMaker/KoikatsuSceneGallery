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
    private const int PreviewCount = 6;

    private readonly StackPanel _body = new() { Spacing = 16, Padding = new Thickness(16) };
    private readonly TextBlock _empty = Caption(UiText.Get("AuthorInspector_Empty"));
    private readonly StackPanel _one = new() { Spacing = 16 };
    private readonly PersonPicture _avatar = new() { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _name = new()
    {
        Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };
    private readonly TextBlock _counts = Caption("");
    private readonly TextBlock _updated = Caption("");
    private readonly Button _open = new()
    {
        Content = UiText.Get("AuthorInspector_Open"),
        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Button _profile = new() { Content = UiText.Get("AuthorInspector_Profile"), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _refresh = new() { Content = UiText.Get("AuthorInspector_Refresh"), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly WrapPanel _previews = new() { Spacing = 6 };

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
        _one.Children.Add(_avatar);
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(_name);
        heading.Children.Add(_counts);
        heading.Children.Add(_updated);
        _one.Children.Add(heading);
        _one.Children.Add(_open);
        _one.Children.Add(_profile);
        _one.Children.Add(_refresh);
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
        foreach (var path in summary.ThumbnailPaths.Take(PreviewCount))
        {
            _previews.Children.Add(new Border
            {
                Width = 88,
                Height = 88,
                CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
                Child = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    Source = new BitmapImage { DecodePixelWidth = 176, UriSource = new Uri(path) },
                },
            });
        }
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
    };
}

using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// Explains that a pane entry needs a plugin, and leads to where plugins are
/// installed. The navigation parameter names the feature: <c>authors</c>,
/// <c>tags</c> or <c>import</c>.
/// </summary>
public sealed partial class FeatureUnavailablePage : Page
{
    public FeatureUnavailablePage()
    {
        InitializeComponent();
    }

    /// <summary>The feature this page is standing in for.</summary>
    public string Feature { get; private set; } = "";

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Feature = e.Parameter as string ?? "";
        var (glyph, key) = Feature switch
        {
            "authors" => (Glyphs.Authors, "Authors"),
            "tags" => (Glyphs.Tags, "Tags"),
            _ => (Glyphs.Import, "Import"),
        };
        FeatureIcon.Glyph = glyph;
        TitleText.Text = UiText.Get($"FeatureUnavailable_{key}Title");
        DescriptionText.Text = UiText.Get($"FeatureUnavailable_{key}Description");
    }

    private void OpenPlugins_Click(object sender, RoutedEventArgs e) =>
        Frame.Navigate(typeof(SettingsPage), "plugins");
}

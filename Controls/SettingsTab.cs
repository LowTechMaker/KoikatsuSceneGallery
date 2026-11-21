using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// One category of the settings page. Holds its content; the page lists the
/// categories on the left (or in a drop-down when narrow) and shows one at a time.
/// </summary>
/// <remarks>
/// Keeps PivotItem's <c>Header</c> so the existing <c>Settings_Tab*.Header</c>
/// strings apply through x:Uid unchanged.
/// </remarks>
public sealed class SettingsTab : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsTab), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingsTab), new PropertyMetadata(Glyphs.Settings));

    public SettingsTab()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = false;
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Tints the app with the selected platform's own colour.
/// </summary>
/// <remarks>
/// A platform's colour is taken from its own branding — measured off its
/// favicon where the site allows it — and a platform with no colour on file
/// keeps the user's Windows accent rather than getting a made-up one.
/// </remarks>
internal static class PlatformAccent
{
    /// <summary>
    /// Brand colours, with where each one came from.
    /// </summary>
    /// <remarks>
    /// Measured rather than remembered wherever that was possible: the value is
    /// the dominant non-neutral pixel of the platform's own favicon. Anything
    /// that could not be measured is marked as such, so a later reader can tell
    /// evidence from a supplied value.
    /// </remarks>
    private static readonly Dictionary<string, string> BrandColors = new(StringComparer.OrdinalIgnoreCase)
    {
        // 1547 of 2304 favicon pixels at full saturation — the blue its logo is
        // drawn in.
        ["pixiv"] = "#0096FA",
        // The icon's yellow, at lightness 0.76. AccentRamp darkens it into a
        // usable fill; white text on the untouched colour is unreadable.
        ["fanbox"] = "#FAF18A",
        // Given by the user, not measured: db.bepis.moe answers 403 to
        // everything without a cleared Cloudflare challenge, favicon included.
        // Lightness 0.36, so the ramp leaves it as it is.
        ["bepisdb"] = "#814936",
    };

    /// <summary>
    /// The colour values Windows derives an accent from. Set for anything that
    /// resolves them later; the brushes already on screen need the pass below.
    /// </summary>
    private static readonly string[] ShadeKeys =
    [
        "SystemAccentColorDark3", "SystemAccentColorDark2", "SystemAccentColorDark1",
        "SystemAccentColor",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3",
    ];

    /// <summary>
    /// Accent brushes the controls actually paint with: fills first, then the
    /// ones used for text and glyphs, which need a different shade to stay
    /// readable against the page rather than against the fill.
    /// </summary>
    private static readonly string[] FillBrushKeys =
    [
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush",
        "AccentFillColorTertiaryBrush", "SystemControlHighlightAccentBrush",
        "SystemControlBackgroundAccentBrush", "NavigationViewSelectionIndicatorForeground",
        "SystemAccentColorBrush",
    ];

    private static readonly string[] TextBrushKeys =
    [
        "AccentTextFillColorPrimaryBrush", "AccentTextFillColorSecondaryBrush",
        "AccentTextFillColorTertiaryBrush",
    ];

    private static AccentShades? _systemShades;

    /// <summary>
    /// Applies the accent for <paramref name="selection"/>, restoring the
    /// user's Windows accent for All and for any platform without a colour.
    /// </summary>
    public static void Apply(CardOriginSelection selection, FrameworkElement? root)
    {
        var shades = ShadesFor(selection);
        var resources = Application.Current.Resources;
        var values = new[]
        {
            shades.Dark3, shades.Dark2, shades.Dark1, shades.Base,
            shades.Light1, shades.Light2, shades.Light3,
        };

        for (var i = 0; i < ShadeKeys.Length; i++)
            resources[ShadeKeys[i]] = ToColor(values[i]);

        // Which shade reads as "the accent" depends on what it sits on: a fill
        // on a dark page has to be lighter than the same fill on a light one,
        // and accent text has to go the other way from its own fill.
        var dark = (root?.ActualTheme ?? ElementTheme.Dark) != ElementTheme.Light;
        var fill = dark ? shades.Light1 : shades.Base;
        var text = dark ? shades.Light3 : shades.Dark2;

        foreach (var key in FillBrushKeys) Repaint(key, fill);
        foreach (var key in TextBrushKeys) Repaint(key, text);
    }

    /// <summary>
    /// Changes an existing brush's colour in place.
    /// </summary>
    /// <remarks>
    /// Replacing the resource entry does nothing to what is on screen: the
    /// brushes were created when the theme dictionaries loaded and every
    /// control already holds a reference to those objects. Mutating
    /// <see cref="SolidColorBrush.Color"/> reaches all of them at once, with no
    /// theme flip and no rebuilt window.
    /// </remarks>
    private static void Repaint(string key, Rgb color)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value)
            && value is SolidColorBrush brush)
        {
            brush.Color = ToColor(color);
        }
    }

    private static AccentShades ShadesFor(CardOriginSelection selection)
    {
        if (!selection.IsAll
            && selection.ProviderId is { } providerId
            && BrandColors.TryGetValue(providerId, out var hex))
        {
            return AccentRamp.From(Rgb.FromHex(hex));
        }
        return SystemShades();
    }

    /// <summary>
    /// The user's own accent, read once before anything overwrites it.
    /// </summary>
    private static AccentShades SystemShades()
    {
        if (_systemShades is { } cached) return cached;

        var settings = new UISettings();
        var shades = new AccentShades(
            Dark3: ToRgb(settings.GetColorValue(UIColorType.AccentDark3)),
            Dark2: ToRgb(settings.GetColorValue(UIColorType.AccentDark2)),
            Dark1: ToRgb(settings.GetColorValue(UIColorType.AccentDark1)),
            Base: ToRgb(settings.GetColorValue(UIColorType.Accent)),
            Light1: ToRgb(settings.GetColorValue(UIColorType.AccentLight1)),
            Light2: ToRgb(settings.GetColorValue(UIColorType.AccentLight2)),
            Light3: ToRgb(settings.GetColorValue(UIColorType.AccentLight3)));
        _systemShades = shades;
        return shades;
    }

    private static Color ToColor(Rgb c) => Color.FromArgb(255, c.R, c.G, c.B);

    private static Rgb ToRgb(Color c) => new(c.R, c.G, c.B);
}

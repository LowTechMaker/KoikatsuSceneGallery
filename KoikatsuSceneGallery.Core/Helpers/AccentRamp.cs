namespace KoikatsuSceneGallery.Helpers;

/// <summary>An sRGB colour, without a dependency on any UI framework.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var value = hex.TrimStart('#');
        return new(
            Convert.ToByte(value[..2], 16),
            Convert.ToByte(value.Substring(2, 2), 16),
            Convert.ToByte(value.Substring(4, 2), 16));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// The seven shades Windows expects an accent colour to come with.
/// </summary>
/// <remarks>
/// Light3 is the lightest. Dark shades carry text on light backgrounds and
/// light shades carry text on dark ones, so all of them have to exist or the
/// controls fall back to the system accent for whichever they are missing and
/// the result is two accents at once.
/// </remarks>
public readonly record struct AccentShades(
    Rgb Dark3, Rgb Dark2, Rgb Dark1, Rgb Base, Rgb Light1, Rgb Light2, Rgb Light3);

/// <summary>
/// Turns one brand colour into an accent palette.
/// </summary>
public static class AccentRamp
{
    /// <summary>
    /// The lightness a base accent is allowed. Accent fills carry white text,
    /// so a colour lighter than this reads as an empty button — FANBOX's brand
    /// yellow sits at 0.76 and is unusable untouched. The hue is what carries
    /// the brand; the lightness is what carries the text.
    /// </summary>
    private const double MinBaseLightness = 0.32;
    private const double MaxBaseLightness = 0.56;

    private const double Step = 0.08;

    /// <summary>The palette for <paramref name="brand"/>.</summary>
    public static AccentShades From(Rgb brand)
    {
        var (h, s, l) = ToHsl(brand);
        var baseL = Math.Clamp(l, MinBaseLightness, MaxBaseLightness);

        return new(
            Dark3: FromHsl(h, s, baseL - Step * 3),
            Dark2: FromHsl(h, s, baseL - Step * 2),
            Dark1: FromHsl(h, s, baseL - Step),
            Base: FromHsl(h, s, baseL),
            Light1: FromHsl(h, s, baseL + Step),
            Light2: FromHsl(h, s, baseL + Step * 2),
            Light3: FromHsl(h, s, baseL + Step * 3));
    }

    internal static (double H, double S, double L) ToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;

        if (Math.Abs(max - min) < 1e-9) return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - r) < 1e-9) h = (g - b) / d + (g < b ? 6 : 0);
        else if (Math.Abs(max - g) < 1e-9) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h / 6, s, l);
    }

    internal static Rgb FromHsl(double h, double s, double l)
    {
        l = Math.Clamp(l, 0, 1);
        if (s <= 0)
        {
            var v = Channel(l);
            return new(v, v, v);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;
        return new(Channel(Hue(p, q, h + 1.0 / 3)), Channel(Hue(p, q, h)), Channel(Hue(p, q, h - 1.0 / 3)));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);
}

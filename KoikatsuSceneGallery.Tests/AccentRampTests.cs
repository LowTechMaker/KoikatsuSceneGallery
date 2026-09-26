using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class AccentRampTests
{
    [Fact]
    public void AColourAlreadyUsableKeepsItsShade()
    {
        // pixiv's brand blue, measured off its favicon. It sits mid-lightness,
        // so the ramp must not move it.
        var shades = AccentRamp.From(Rgb.FromHex("#0096FA"));

        Assert.Equal("#0096FA", shades.Base.ToHex());
    }

    [Fact]
    public void AColourTooLightToCarryTextIsDarkened()
    {
        // FANBOX's icon yellow is lightness 0.76; white text on it is
        // unreadable, so the base is pulled into the usable band.
        var brand = Rgb.FromHex("#FAF18A");
        var shades = AccentRamp.From(brand);

        var (_, _, brandLightness) = AccentRamp.ToHsl(brand);
        var (_, _, baseLightness) = AccentRamp.ToHsl(shades.Base);

        Assert.True(brandLightness > 0.7);
        Assert.True(baseLightness < brandLightness);
        Assert.InRange(baseLightness, 0.32, 0.57);
    }

    [Fact]
    public void TheHueSurvivesTheAdjustment()
    {
        // Darkening must keep the brand recognisable; only the lightness moves.
        var brand = Rgb.FromHex("#FAF18A");
        var (brandHue, _, _) = AccentRamp.ToHsl(brand);
        var (baseHue, _, _) = AccentRamp.ToHsl(AccentRamp.From(brand).Base);

        Assert.Equal(brandHue, baseHue, 2);
    }

    [Fact]
    public void ShadesRunFromDarkToLight()
    {
        var s = AccentRamp.From(Rgb.FromHex("#0096FA"));
        var lightness = new[] { s.Dark3, s.Dark2, s.Dark1, s.Base, s.Light1, s.Light2, s.Light3 }
            .Select(c => AccentRamp.ToHsl(c).L)
            .ToArray();

        for (var i = 1; i < lightness.Length; i++)
            Assert.True(lightness[i] > lightness[i - 1], $"shade {i} is not lighter than {i - 1}");
    }

    [Fact]
    public void AGreyStaysGrey()
    {
        // No hue to preserve; the ramp must not invent one.
        var shades = AccentRamp.From(new Rgb(128, 128, 128));

        Assert.Equal(shades.Base.R, shades.Base.G);
        Assert.Equal(shades.Base.G, shades.Base.B);
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    public void TheExtremesStayInRange(string hex)
    {
        // Clamping happens before the steps are applied, so neither end can
        // wrap around into the opposite shade.
        var s = AccentRamp.From(Rgb.FromHex(hex));

        Assert.InRange(AccentRamp.ToHsl(s.Dark3).L, 0.0, 1.0);
        Assert.InRange(AccentRamp.ToHsl(s.Light3).L, 0.0, 1.0);
    }

    [Fact]
    public void HexRoundTrips()
        => Assert.Equal("#0096FA", Rgb.FromHex("#0096FA").ToHex());
}

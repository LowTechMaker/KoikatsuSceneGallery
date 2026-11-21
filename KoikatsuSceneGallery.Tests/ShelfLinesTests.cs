using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class ShelfLinesTests
{
    private const double Spacing = 8;

    [Fact]
    public void OneLineTakesAsManyTilesAsItsWidthAllows()
    {
        var widths = new[] { 100.0, 100, 100, 100 };

        Assert.Equal(3, ShelfLines.Fit(widths, 320, Spacing, 1));
    }

    [Fact]
    public void TwoLinesTakeTheOverflowOfTheFirst()
    {
        var widths = new[] { 100.0, 100, 100, 100 };

        Assert.Equal(4, ShelfLines.Fit(widths, 320, Spacing, 2));
    }

    [Fact]
    public void TenTilesOnTwoLinesKeepThreePerLine()
    {
        var widths = new double[10];
        Array.Fill(widths, 100);

        Assert.Equal(6, ShelfLines.Fit(widths, 320, Spacing, 2));
    }

    [Fact]
    public void ZeroMaxLinesMeansNoLimit()
    {
        var widths = new double[10];
        Array.Fill(widths, 100);

        Assert.Equal(widths.Length, ShelfLines.Fit(widths, 50, Spacing, 0));
    }

    [Fact]
    public void NotMeasuredYetPutsOneTilePerLine()
    {
        var widths = new double[5];
        Array.Fill(widths, 100);

        Assert.Equal(2, ShelfLines.Fit(widths, 0, Spacing, 2));
    }

    [Fact]
    public void AFirstTileWiderThanTheShelfStillGoesOnTheFirstLine()
    {
        var widths = new[] { 500.0, 100, 100 };

        Assert.Equal(1, ShelfLines.Fit(widths, 320, Spacing, 1));
        Assert.Equal(3, ShelfLines.Fit(widths, 320, Spacing, 2));
    }

    [Fact]
    public void NoTilesFitOnNoLines()
    {
        Assert.Equal(0, ShelfLines.Fit(Array.Empty<double>(), 320, Spacing, 1));
    }

    [Fact]
    public void AnExactFitStaysOnOneLine()
    {
        var widths = new[] { 156.0, 156 };

        Assert.Equal(2, ShelfLines.Fit(widths, 320, Spacing, 1));
    }
}

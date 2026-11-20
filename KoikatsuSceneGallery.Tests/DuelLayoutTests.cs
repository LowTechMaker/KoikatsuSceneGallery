using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class DuelLayoutTests
{
    private const double Chrome = 120;

    [Fact]
    public void AtTheLiveRunsWindowThePanelTakesAboutAThirdOfTheHeight()
    {
        // The wave 4 live run: a 1440x753 window, the page about 1055 px wide.
        var fit = DuelLayout.Fit(1055, 753, Chrome);

        Assert.False(fit.Stacked);
        Assert.True(Chrome + fit.PictureHeight <= 753 * DuelLayout.HeightShare + 0.5, $"{Chrome + fit.PictureHeight}");
        Assert.Equal(16.0 / 9, fit.CardWidth / fit.PictureHeight, 3);
    }

    [Fact]
    public void ATallWindowIsLimitedByTheWidthInstead()
    {
        var fit = DuelLayout.Fit(1055, 2000, Chrome);

        Assert.Equal((DuelLayout.MaxWidthBoth - DuelLayout.Gap) / 2, fit.CardWidth, 3);
        Assert.Equal(fit.CardWidth * 9 / 16, fit.PictureHeight, 3);
    }

    [Fact]
    public void TheTwoCardsAreNeverWiderThanTheCap()
    {
        var fit = DuelLayout.Fit(3000, 3000, Chrome);

        Assert.True(2 * fit.CardWidth + DuelLayout.Gap <= DuelLayout.MaxWidthBoth + 0.5);
    }

    [Fact]
    public void ALowWindowNeverShrinksThePictureBelowTheMinimum()
    {
        var fit = DuelLayout.Fit(1055, 300, Chrome);

        Assert.Equal(DuelLayout.MinPictureHeight, fit.PictureHeight, 3);
        Assert.Equal(DuelLayout.MinPictureHeight * 16 / 9, fit.CardWidth, 3);
    }

    [Fact]
    public void ANarrowPanelStacksAndSharesTheHeightBetweenTwoPictures()
    {
        // 1200 rather than 900: at 900 the minimum picture height alone pushes
        // the panel past a third of the window, whatever the layout does.
        var fit = DuelLayout.Fit(400, 1200, Chrome);

        Assert.True(fit.Stacked);
        Assert.True(Chrome + 2 * fit.PictureHeight <= 1200 * DuelLayout.HeightShare + 0.5);
        Assert.True(fit.CardWidth <= 400);
    }

    [Fact]
    public void AWidePanelBelowTheOldBreakpointNoLongerStacks()
    {
        var fit = DuelLayout.Fit(600, 1080, Chrome);

        Assert.False(fit.Stacked);
    }

    [Fact]
    public void TheCardsStay16By9AtEverySize()
    {
        foreach (var width in new[] { 300.0, 479, 480, 639, 640, 900, 1055, 2000 })
            foreach (var height in new[] { 200.0, 500, 753, 1200 })
            {
                var fit = DuelLayout.Fit(width, height, Chrome);
                Assert.Equal(16.0 / 9, fit.CardWidth / fit.PictureHeight, 3);
            }
    }

    [Fact]
    public void NothingMeasuredYetGivesTheMinimumNotZero()
    {
        var fit = DuelLayout.Fit(0, 0, Chrome);

        Assert.Equal(DuelLayout.MinPictureHeight, fit.PictureHeight, 3);
    }
}

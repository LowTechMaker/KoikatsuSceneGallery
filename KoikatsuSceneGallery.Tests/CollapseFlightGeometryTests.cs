using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class CollapseFlightGeometryTests
{
    private static readonly FlightRect Target = new(100, 100, 20, 20);

    [Fact]
    public void ATileLeftOfAndAboveTheTargetTravelsRightAndDown()
    {
        var (dx, dy) = CollapseFlightGeometry.CenterDelta(new(0, 0, 10, 10), Target);

        Assert.Equal(105, dx);
        Assert.Equal(105, dy);
    }

    [Fact]
    public void ATileRightOfTheTargetTravelsLeft()
    {
        var (dx, _) = CollapseFlightGeometry.CenterDelta(new(200, 105, 10, 10), Target);

        Assert.True(dx < 0);
    }

    [Fact]
    public void ATileAlreadyOnTheTargetDoesNotMove()
    {
        var (dx, dy) = CollapseFlightGeometry.CenterDelta(Target, Target);

        Assert.Equal(0, dx);
        Assert.Equal(0, dy);
    }

    [Fact]
    public void ScaleTakesTheTighterOfTheTwoAxes()
    {
        // 20/100 across, 20/40 down: the width is the binding constraint.
        Assert.Equal(0.2, CollapseFlightGeometry.UniformScale(new(0, 0, 100, 40), Target), 6);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(-5, 10)]
    public void ADegenerateTileKeepsItsSizeInsteadOfDividingByZero(double width, double height)
        => Assert.Equal(1, CollapseFlightGeometry.UniformScale(new(0, 0, width, height), Target));

    [Fact]
    public void ASingleTileHasNoStagger()
        => Assert.Equal(TimeSpan.Zero,
            CollapseFlightGeometry.StaggerFor(1, TimeSpan.FromMilliseconds(18), TimeSpan.FromMilliseconds(120)));

    [Fact]
    public void ASmallBatchGetsTheFullPerItemStagger()
        => Assert.Equal(TimeSpan.FromMilliseconds(18),
            CollapseFlightGeometry.StaggerFor(2, TimeSpan.FromMilliseconds(18), TimeSpan.FromMilliseconds(120)));

    [Fact]
    public void AFullViewportStillFinishesInsideTheBudget()
    {
        var perItem = TimeSpan.FromMilliseconds(18);
        var budget = TimeSpan.FromMilliseconds(120);

        var stagger = CollapseFlightGeometry.StaggerFor(24, perItem, budget);

        Assert.True(stagger < perItem);
        Assert.True(stagger * 23 <= budget);
    }

    [Fact]
    public void TilesFlyNearestFirst()
    {
        FlightRect[] origins = [new(0, 0, 10, 10), new(95, 95, 10, 10), new(400, 400, 10, 10)];

        Assert.Equal([1, 0, 2], CollapseFlightGeometry.OrderByDistance(origins, Target));
    }

    [Fact]
    public void EquidistantTilesKeepTheirOriginalOrder()
    {
        FlightRect[] origins = [new(105, 55, 10, 10), new(105, 155, 10, 10)];

        Assert.Equal([0, 1], CollapseFlightGeometry.OrderByDistance(origins, Target));
    }

    [Fact]
    public void AnEmptyBatchOrdersToNothing()
        => Assert.Empty(CollapseFlightGeometry.OrderByDistance([], Target));
}

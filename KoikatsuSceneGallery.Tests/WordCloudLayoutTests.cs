using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class WordCloudLayoutTests
{
    private const double Width = 1200;
    private const double Height = 600;

    /// <summary>Sizes roughly like a real cloud: a few big words, many small.</summary>
    private static (double Width, double Height)[] Words(int count) =>
        [.. Enumerable.Range(0, count).Select(i =>
        {
            var scale = 1.0 - (double)i / count;
            return (60 + 260 * scale, 20 + 34 * scale);
        })];

    private static FlightRect[] Placed(IReadOnlyList<FlightRect?> results) =>
        [.. results.Where(r => r is not null).Select(r => r!.Value)];

    [Fact]
    public void NothingOverlaps()
    {
        var placed = Placed(WordCloudLayout.Place(Words(60), Width, Height));

        Assert.True(placed.Length > 1);
        for (var i = 0; i < placed.Length; i++)
            for (var j = i + 1; j < placed.Length; j++)
                Assert.False(Intersects(placed[i], placed[j]), $"{i} overlaps {j}");
    }

    [Fact]
    public void EverythingStaysInsideTheCanvas()
    {
        foreach (var rect in Placed(WordCloudLayout.Place(Words(60), Width, Height)))
        {
            Assert.InRange(rect.X, 0, Width - rect.Width);
            Assert.InRange(rect.Y, 0, Height - rect.Height);
        }
    }

    [Fact]
    public void TheBiggestWordLandsNearTheMiddle()
    {
        var first = WordCloudLayout.Place(Words(60), Width, Height)[0];

        Assert.NotNull(first);
        Assert.InRange(first!.Value.CenterX, Width / 2 - 40, Width / 2 + 40);
        Assert.InRange(first.Value.CenterY, Height / 2 - 40, Height / 2 + 40);
    }

    /// <summary>
    /// The point of the whole exercise: the silhouette must not be the full
    /// rectangle a wrapping panel would produce.
    /// </summary>
    [Fact]
    public void TheOutlineIsARaggedBlobRatherThanFullWidthRows()
    {
        var placed = Placed(WordCloudLayout.Place(Words(60), Width, Height));

        // Group by row band and compare how far each band reaches.
        var rowExtents = placed
            .GroupBy(r => (int)(r.CenterY / 60))
            .Select(g => g.Max(r => r.X + r.Width) - g.Min(r => r.X))
            .OrderBy(w => w)
            .ToArray();

        Assert.True(rowExtents.Length >= 3);
        // A wrapping panel makes every row the same full width; a cloud's rows
        // differ a lot, with the shortest well under the widest.
        Assert.True(rowExtents[0] < rowExtents[^1] * 0.75,
            $"rows are too uniform: {string.Join(", ", rowExtents.Select(w => (int)w))}");
    }

    [Fact]
    public void PlacementIsDeterministic()
        => Assert.Equal(
            WordCloudLayout.Place(Words(40), Width, Height),
            WordCloudLayout.Place(Words(40), Width, Height));

    [Fact]
    public void ResultsLineUpWithTheInput()
    {
        var results = WordCloudLayout.Place(Words(25), Width, Height);

        Assert.Equal(25, results.Count);
    }

    [Fact]
    public void AWordTooBigForTheCanvasIsSkippedWithoutBreakingTheRest()
    {
        (double, double)[] sizes = [(300, 40), (5000, 40), (200, 30)];

        var results = WordCloudLayout.Place(sizes, Width, Height);

        Assert.NotNull(results[0]);
        Assert.Null(results[1]);
        Assert.NotNull(results[2]);
    }

    [Fact]
    public void AnEmptyCloudPlacesNothing()
        => Assert.Empty(WordCloudLayout.Place([], Width, Height));

    [Theory]
    [InlineData(0, 600)]
    [InlineData(1200, 0)]
    [InlineData(-10, -10)]
    public void ACanvasWithNoRoomPlacesNothing(double width, double height)
        => Assert.All(WordCloudLayout.Place(Words(10), width, height), Assert.Null);

    [Fact]
    public void PaddingIsHonouredBetweenNeighbours()
    {
        const double padding = 20;
        var placed = Placed(WordCloudLayout.Place(Words(30), Width, Height, padding));

        for (var i = 0; i < placed.Length; i++)
            for (var j = i + 1; j < placed.Length; j++)
                Assert.False(Intersects(Inflate(placed[i], padding / 2), Inflate(placed[j], padding / 2)));
    }

    private static FlightRect Inflate(FlightRect rect, double by)
        => new(rect.X - by, rect.Y - by, rect.Width + by * 2, rect.Height + by * 2);

    private static bool Intersects(FlightRect a, FlightRect b)
        => a.X < b.X + b.Width && b.X < a.X + a.Width
           && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
}

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class DiscoveryShuffleTests
{
    [Fact]
    public void IncrementalDrawingCoversLargePoolExactlyOnceAndStops()
    {
        var shuffle = new DiscoveryShuffle<int>(new Random(42));
        shuffle.Reset(Enumerable.Range(0, 10003));
        var history = new List<int>();
        while (shuffle.Remaining > 0) history.AddRange(shuffle.Take(40));
        Assert.Equal(10003, history.Count);
        Assert.Equal(10003, history.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 10003), history.Order());
        Assert.Empty(shuffle.Take(40));
        Assert.Equal(1, shuffle.Round);
    }

    [Fact]
    public void NextRoundPreservesCallerHistoryAndAvoidsImmediateRepeat()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            var shuffle = new DiscoveryShuffle<int>(new Random(seed));
            shuffle.Reset([1, 2, 3]);
            var first = shuffle.Take(10);
            shuffle.StartRound([1, 2, 3]);
            var second = shuffle.Take(10);
            Assert.Equal(3, first.Count);
            Assert.Equal(3, second.Distinct().Count());
            Assert.NotEqual(first[^1], second[0]);
            Assert.Equal(2, shuffle.Round);
        }
    }

    [Fact]
    public void LiveChangesRemoveDeletedFilesAndDoNotRedrawReturnedFiles()
    {
        var shuffle = new DiscoveryShuffle<string>(new Random(1), StringComparer.OrdinalIgnoreCase);
        shuffle.Reset(["a", "b", "c"]);
        var first = shuffle.Take(1)[0];
        shuffle.Synchronize([]);
        shuffle.Synchronize(["A", "b", "c", "d"]);
        var remaining = shuffle.Take(99);
        Assert.DoesNotContain(remaining, item => StringComparer.OrdinalIgnoreCase.Equals(item, first));
        Assert.Equal(3, remaining.Count);
        Assert.Contains("d", remaining);
        Assert.Equal(4, shuffle.Drawn);
    }

    [Fact]
    public void ResetChangesScopeAndClearsRoundProgress()
    {
        var shuffle = new DiscoveryShuffle<int>(new Random(0));
        shuffle.Reset([1, 2, 3]);
        shuffle.Take(2);
        shuffle.Reset([8, 8, 9]);
        Assert.Equal(0, shuffle.Drawn);
        Assert.Equal(2, shuffle.Total);
        Assert.Equal(new[] { 8, 9 }, shuffle.Take(50).Order());
    }

    [Fact]
    public void EmptyAndSingleCardPoolsRequireExplicitNextRound()
    {
        var shuffle = new DiscoveryShuffle<int>(new Random(0));
        shuffle.Reset([]);
        Assert.Empty(shuffle.Take(40));
        shuffle.Reset([1]);
        Assert.Equal(new[] { 1 }, shuffle.Take(40));
        Assert.Empty(shuffle.Take(40));
        shuffle.StartRound([1]);
        Assert.Equal(new[] { 1 }, shuffle.Take(40));
    }
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardGroupingTests
{
    private static ImportItem Card(string folder, string name, ulong? hash, float[]? histogram = null)
        => new() { SourceFilePath = Path.Combine(folder, name), PHash = hash, ColorHistogram = histogram };

    [Theory]
    [InlineData(0UL, 0UL, 0)]
    [InlineData(0UL, ulong.MaxValue, 64)]
    [InlineData(0UL, 1UL << 63, 1)]
    public void HammingDistanceIncludesEveryBit(ulong a, ulong b, int expected)
        => Assert.Equal(expected, ImageFingerprintComparison.HammingDistance(a, b));

    [Fact]
    public void HistogramComparisonRetainsDegenerateAndCorrelationRules()
    {
        Assert.Equal(0f, ImageFingerprintComparison.HistogramCorrelation([], []));
        Assert.Equal(0f, ImageFingerprintComparison.HistogramCorrelation([1], [1, 2]));
        Assert.Equal(0f, ImageFingerprintComparison.HistogramCorrelation([1, 1], [1, 1]));
        Assert.Equal(1f, ImageFingerprintComparison.HistogramCorrelation([1, 2, 3], [2, 4, 6]));
        Assert.Equal(-1f, ImageFingerprintComparison.HistogramCorrelation([1, 2, 3], [3, 2, 1]));
    }

    [Theory]
    [InlineData(22, false, true)]
    [InlineData(23, false, false)]
    [InlineData(28, true, true)]
    [InlineData(29, true, false)]
    public void RelatednessPreservesFolderSpecificDistanceThreshold(int distance, bool sameFolder, bool expected)
    {
        var first = Card("FOLDER", "a.png", 0);
        var second = Card(sameFolder ? "folder" : "other", "b.png", (1UL << distance) - 1);
        Assert.Equal(expected, CardGroupingService.AreVisuallyRelated(first, second));
    }

    [Fact]
    public void HistogramsCanRejectCloseHashesAndMissingHistogramKeepsHashVerdict()
    {
        var first = Card("one", "a", 0, [1, 2, 3]);
        var second = Card("two", "b", 0, [3, 2, 1]);
        Assert.False(CardGroupingService.AreVisuallyRelated(first, second));
        second.ColorHistogram = null;
        Assert.True(CardGroupingService.AreVisuallyRelated(first, second));
        second.PHash = null;
        Assert.False(CardGroupingService.AreVisuallyRelated(first, second));
    }

    [Fact]
    public void MissingFingerprintsKeepUnknownVerdictDistinctFromFalse()
    {
        var unknown = Card("one", "a", null);
        var other = Card("two", "b", null);
        Assert.False(CardGroupingService.ShouldGroupAsArtwork([]));
        Assert.False(CardGroupingService.ShouldGroupAsArtwork([unknown]));
        Assert.Null(CardGroupingService.ShouldGroupAsArtwork([unknown, other]));
        other.PHash = 0;
        Assert.False(CardGroupingService.ShouldGroupAsArtwork([unknown, other]));
    }

    [Fact]
    public void ArtworkDecisionUsesThirtyPercentOfPairsRatherThanMajority()
    {
        ImportItem[] cards = [Card("one", "a", 0), Card("two", "b", (1UL << 20) - 1),
            Card("three", "c", (1UL << 40) - 1), Card("four", "d", ulong.MaxValue)];
        // Only a/b and b/c qualify: 2 of 6 pairs passes ceil(6 * 0.3).
        Assert.True(CardGroupingService.ShouldGroupAsArtwork(cards));
        cards[2].PHash = ulong.MaxValue ^ 1;
        cards[3].PHash = null;
        Assert.False(CardGroupingService.ShouldGroupAsArtwork(cards));
    }

    [Fact]
    public void VisualGroupsUseTransitiveConnectionsAndPreserveInstancesAndOrder()
    {
        var a = Card("one", "z.png", 0);
        var b = Card("two", "y.png", (1UL << 20) - 1);
        var c = Card("three", "x.png", (1UL << 40) - 1);
        var isolated = Card("four", "a.png", null);
        var groups = CardGroupingService.GroupByVisualSimilarity([isolated, a, b, c]);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { a, b, c }, groups[0]);
        Assert.Same(isolated, Assert.Single(groups[1]));
        Assert.Empty(CardGroupingService.GroupByVisualSimilarity([]));
    }
}

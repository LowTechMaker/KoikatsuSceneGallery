using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class GalleryShuffleQueueTests
{
    private static GalleryShuffleQueue Create(int size = 3) => new(size, bound => bound - 1);
    private static object[] Ordered(GalleryShuffleQueue queue, int count = int.MaxValue) =>
        queue.GetDisplaySet(count).OrderBy(x => x,
            Comparer<object>.Create(queue.Comparer.Compare)).ToArray();

    [Fact]
    public void BuildFiltersNullsAndCapsPoolWithoutChangingSource()
    {
        object?[] source = [null, 1, 2, 3, 4, 5];
        var queue = Create(2);
        queue.Build(source, x => (int)x % 2 == 1);
        Assert.Equal(new object[] { 1, 3 }, Ordered(queue));
        Assert.Equal(new object?[] { null, 1, 2, 3, 4, 5 }, source);
    }

    [Fact]
    public void ShuffleUsesDescendingFisherYatesBounds()
    {
        var bounds = new List<int>();
        var queue = new GalleryShuffleQueue(3, bound => { bounds.Add(bound); return 0; });
        queue.Build(new object[] { 1, 2, 3, 4 }, _ => true);
        Assert.Equal(new[] { 4, 3, 2 }, bounds);
        Assert.Equal(new object[] { 2, 3, 4 }, Ordered(queue));
    }

    [Fact]
    public void AdvanceKeepsTailAndUsesUnseenCandidatesBeforeStartingNewCycle()
    {
        object[] source = [1, 2, 3, 4, 5];
        var queue = Create();
        queue.Build(source, _ => true);
        queue.Advance(source, _ => true, 2);
        Assert.Equal(new object[] { 3, 4, 5 }, Ordered(queue));
        queue.Advance(source, _ => true, 2);
        Assert.Equal(new object[] { 5, 1, 2 }, Ordered(queue));
    }

    [Fact]
    public void PartialRefillDoesNotStartNewCycleEarly()
    {
        object[] source = [1, 2, 3, 4];
        var queue = Create();
        queue.Build(source, _ => true);
        queue.Advance(source, _ => true, 2);
        Assert.Equal(new object[] { 3, 4 }, Ordered(queue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveDisplayDoesNotAdvanceOrEnumerate(int count)
    {
        var queue = Create();
        queue.Build(new object[] { 1, 2, 3 }, _ => true);
        queue.Advance(new object[] { 4 }, _ => throw new InvalidOperationException(), count);
        Assert.Equal(new object[] { 1, 2, 3 }, Ordered(queue));
        Assert.Empty(queue.GetDisplaySet(count));
    }

    [Fact]
    public void DisplaySetIsSnapshotAndExistingComparerTracksRebuildAndClear()
    {
        var queue = Create();
        var comparer = queue.Comparer;
        queue.Build(new object[] { 1, 2, 3 }, _ => true);
        var snapshot = queue.GetDisplaySet(2);
        queue.Build(new object[] { 3, 2, 1 }, _ => true);
        Assert.True(snapshot.SetEquals(new object[] { 1, 2 }));
        Assert.True(comparer.Compare(3, 1) < 0);
        queue.Clear();
        Assert.Empty(Ordered(queue));
        Assert.Equal(0, comparer.Compare(3, 1));
        queue.Build(new object[] { 1 }, _ => true);
        queue.Advance(new object[] { 1 }, _ => true, 100);
        Assert.Equal(new object[] { 1 }, Ordered(queue));
    }

    [Fact]
    public void AdvanceUsesCurrentSourceAndFilterButRetainsExistingTail()
    {
        var queue = Create();
        queue.Build(new object[] { 1, 2, 3 }, _ => true);
        queue.Advance(new object[] { 4, 5, 6 }, x => (int)x != 5, 2);
        Assert.Equal(new object[] { 3, 4, 6 }, Ordered(queue));
    }

    [Fact]
    public void EmptyQueueDoesNotInvokeFilterWhenAdvanced()
    {
        var queue = Create();
        queue.Build(Array.Empty<object>(), _ => true);
        queue.Advance(new object[] { 1 }, _ => throw new InvalidOperationException(), 1);
        Assert.Empty(Ordered(queue));
    }

    [Fact]
    public void RebuildResetsUsedCardsAndPreservesItemInstances()
    {
        object first = new(), second = new(), third = new();
        object[] source = [first, second, third];
        var queue = Create(2);
        queue.Build(source, _ => true);
        queue.Advance(source, _ => true, 1);
        Assert.Same(third, Ordered(queue)[1]);
        queue.Build(source, _ => true);
        queue.Advance(source, _ => true, 1);
        Assert.Same(second, Ordered(queue)[0]);
        Assert.Same(third, Ordered(queue)[1]);
    }
}

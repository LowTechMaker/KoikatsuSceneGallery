using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class DiscoveryAntiClusteringTests
{
    private sealed record Item(int Id, long Size, string Folder);

    private static DiscoveryClusterKey Key(Item item) => new(item.Size, item.Folder);

    private static void Spread(IList<Item> batch, int columns, params Item[] tail) =>
        DiscoveryAntiClustering.Spread(batch, Key, columns, tail);

    /// <summary>Counts adjacent pairs that share a folder and sit within the size tolerance.</summary>
    private static int Conflicts(IReadOnlyList<Item> grid, int columns)
    {
        int count = 0;
        for (int i = 0; i < grid.Count; i++)
        {
            if (i % columns != 0 && Similar(grid[i], grid[i - 1])) count++;
            if (i - columns >= 0 && Similar(grid[i], grid[i - columns])) count++;
        }
        return count;
    }

    private static bool Similar(Item a, Item b) =>
        string.Equals(a.Folder, b.Folder, StringComparison.OrdinalIgnoreCase)
        && Math.Abs(a.Size - b.Size) <= Math.Max(4096, Math.Min(a.Size, b.Size) / 100);

    [Fact]
    public void ReorderingNeverAddsLosesOrDuplicatesACard()
    {
        var random = new Random(7);
        for (int trial = 0; trial < 200; trial++)
        {
            var original = Enumerable.Range(0, 40)
                .Select(i => new Item(i, random.Next(4) * 2_000_000L + 500_000, $"f{random.Next(3)}"))
                .ToList();
            var batch = original.ToList();

            Spread(batch, 1 + random.Next(6));

            Assert.Equal(original.Count, batch.Count);
            Assert.Equal(original.Select(i => i.Id).Order(), batch.Select(i => i.Id).Order());
            Assert.Equal(batch.Count, batch.Select(i => i.Id).Distinct().Count());
        }
    }

    [Fact]
    public void SameRowNeighboursAreSeparated()
    {
        // Two clusters interleaved so that a plain draw puts look-alikes side by side.
        List<Item> batch =
        [
            new(0, 1_000_000, "a"), new(1, 1_000_000, "a"), new(2, 1_000_000, "a"), new(3, 1_000_000, "a"),
            new(4, 9_000_000, "b"), new(5, 9_000_000, "b"), new(6, 9_000_000, "b"), new(7, 9_000_000, "b"),
        ];
        int before = Conflicts(batch, 8);

        Spread(batch, 8);

        Assert.True(Conflicts(batch, 8) < before);
    }

    [Fact]
    public void NeighbourDirectlyAboveIsSeparatedInAFourColumnGrid()
    {
        // Rows of four: without spreading, every card sits above an identical one.
        List<Item> batch =
        [
            new(0, 1_000_000, "a"), new(1, 9_000_000, "b"), new(2, 1_000_000, "a"), new(3, 9_000_000, "b"),
            new(4, 1_000_000, "a"), new(5, 9_000_000, "b"), new(6, 1_000_000, "a"), new(7, 9_000_000, "b"),
        ];
        int before = Conflicts(batch, 4);

        Spread(batch, 4);

        Assert.True(Conflicts(batch, 4) < before);
    }

    [Fact]
    public void FirstSlotIsCheckedAgainstAlreadyDisplayedCards()
    {
        var tail = new Item(99, 1_000_000, "a");
        List<Item> batch = [new(0, 1_000_000, "a"), new(1, 9_000_000, "b"), new(2, 9_000_000, "b")];

        // The tail fills column 0..0 of a one-wide grid, so batch[0] lands directly under it.
        Spread(batch, 1, tail);

        Assert.NotEqual(0, batch[0].Id);
    }

    [Fact]
    public void SingleFolderLibraryStillSpreadsBySize()
    {
        // Every pair shares a folder. A boolean "no conflict" rule would find no candidate at all;
        // the weighted score lets file size still decide.
        List<Item> batch =
        [
            new(0, 1_000_000, "only"), new(1, 1_000_000, "only"),
            new(2, 9_000_000, "only"), new(3, 9_000_000, "only"),
        ];
        int before = Conflicts(batch, 4);

        Spread(batch, 4);

        Assert.True(Conflicts(batch, 4) < before);
        Assert.Equal([0, 1, 2, 3], batch.Select(i => i.Id).Order());
    }

    [Fact]
    public void EmptySingleAndDegenerateInputsAreLeftAlone()
    {
        List<Item> empty = [];
        Spread(empty, 4);
        Assert.Empty(empty);

        List<Item> single = [new(0, 1_000_000, "a")];
        Spread(single, 4);
        Assert.Equal([0], single.Select(i => i.Id));

        List<Item> zeroColumns = [new(0, 1_000_000, "a"), new(1, 1_000_000, "a")];
        Spread(zeroColumns, 0);
        Assert.Equal([0, 1], zeroColumns.Select(i => i.Id));

        // Unknown sizes must not be treated as "identical to each other".
        List<Item> unsized = [new(0, 0, "a"), new(1, 0, "b"), new(2, 0, "c")];
        Spread(unsized, 4);
        Assert.Equal([0, 1, 2], unsized.Select(i => i.Id).Order());
    }

    [Fact]
    public void FullyHomogeneousBatchKeepsItsOriginalOrder()
    {
        List<Item> batch =
        [
            new(0, 1_000_000, "a"), new(1, 1_000_000, "a"),
            new(2, 1_000_000, "a"), new(3, 1_000_000, "a"),
        ];

        Spread(batch, 2);

        // Nothing in reach is any better, so no card is moved for the sake of moving it.
        Assert.Equal([0, 1, 2, 3], batch.Select(i => i.Id));
    }
}

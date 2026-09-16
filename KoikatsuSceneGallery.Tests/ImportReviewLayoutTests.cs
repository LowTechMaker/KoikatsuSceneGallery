using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class ImportReviewLayoutTests
{
    private sealed record Card(int Id, bool Identified, string Group, bool Unavailable = false);
    private static IReadOnlyList<ImportReviewLayoutRow<Card>> Build(IEnumerable<Card> cards, int columns = 3)
        => ImportReviewLayout.Build(cards, c => c.Identified ? ImportReviewSection.Identified
            : c.Unavailable ? ImportReviewSection.Unavailable : ImportReviewSection.Unidentified, c => c.Group, columns);
    private static IEnumerable<Card> Rendered(IEnumerable<ImportReviewLayoutRow<Card>> rows)
        => rows.Where(r => r.Kind is ImportReviewRowKind.ListItem or ImportReviewRowKind.GridRow).SelectMany(r => r.Items);

    [Fact]
    public void MixedBatchPutsAttentionFirstWithoutDuplicatingImages()
    {
        Card[] cards = [new(1, true, "post:a"), new(2, false, "post:deleted"),
            new(3, true, "post:a"), new(4, false, "folder:unknown")];
        var rows = Build(cards);
        Assert.Equal(new[] { 2, 4, 1, 3 }, Rendered(rows).Select(c => c.Id));
        Assert.All(rows.Where(r => r.Kind == ImportReviewRowKind.ListItem), r => Assert.Single(r.Items));
        Assert.Equal(3, rows.Count(r => r.Kind == ImportReviewRowKind.Section));
    }

    [Fact]
    public void GridRowsStayWithinColumnsAndNeverCombineDifferentPosts()
    {
        var cards = Enumerable.Range(0, 11).Select(i => new Card(i, true, i < 8 ? "a" : "b")).ToArray();
        var rows = Build(cards).Where(r => r.Kind == ImportReviewRowKind.GridRow).ToArray();
        Assert.Equal(new[] { 3, 3, 2, 3 }, rows.Select(r => r.Items.Count));
        Assert.All(rows, r => Assert.Single(r.Items.Select(c => c.Group).Distinct()));
        Assert.Equal(cards, Rendered(rows));
    }

    [Fact]
    public void IdentifyingAndUndoingMoveAnItemBetweenLayoutsExactlyOnce()
    {
        var card = new Card(1, false, "a");
        Assert.Contains(Build([card]), r => r.Kind == ImportReviewRowKind.ListItem);
        var identified = Build([card with { Identified = true }]);
        Assert.DoesNotContain(identified, r => r.Kind == ImportReviewRowKind.ListItem);
        Assert.Single(Rendered(identified));
        Assert.Contains(Build([card]), r => r.Kind == ImportReviewRowKind.ListItem);
    }

    [Fact]
    public void ResizeKeepsEveryImageAndInputOrder()
    {
        var cards = Enumerable.Range(0, 1000).Select(i => new Card(i, true, "a")).ToArray();
        foreach (var columns in new[] { 1, 2, 4, 6 }) Assert.Equal(cards, Rendered(Build(cards, columns)));
    }

    [Theory]
    [InlineData(180, 1)] [InlineData(440, 2)] [InlineData(1100, 5)] [InlineData(2500, 6)]
    public void ColumnCountAdaptsToViewport(double width, int columns)
        => Assert.Equal(columns, ImportReviewLayout.ColumnsForWidth(width));

    [Fact]
    public void EmptyBatchStillProvidesAllSectionHeaders()
    {
        var rows = Build([]);
        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => { Assert.Equal(ImportReviewRowKind.Section, row.Kind); Assert.Empty(row.Items); });
    }

    [Fact]
    public void UnavailablePostsFollowUnidentifiedAndPrecedeTheGrid()
    {
        Card[] cards = [new(1, true, "known"), new(2, false, "deleted", true), new(3, false, "unknown")];
        var rows = Build(cards);
        Assert.Equal(new[] { 3, 2, 1 }, Rendered(rows).Select(c => c.Id));
        Assert.Equal(new[] { ImportReviewSection.Unidentified, ImportReviewSection.Unavailable, ImportReviewSection.Identified },
            rows.Where(r => r.Kind == ImportReviewRowKind.Section).Select(r => r.Section));
        Assert.Equal(ImportReviewRowKind.ListItem, rows.Single(r => r.Items.Count == 1 && r.Items[0].Id == 2
            && r.Kind is ImportReviewRowKind.GridRow or ImportReviewRowKind.ListItem).Kind);
    }
}

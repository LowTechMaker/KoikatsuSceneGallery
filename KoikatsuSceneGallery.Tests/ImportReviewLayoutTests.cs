using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class ImportReviewLayoutTests
{
    private sealed record Card(int Id, ImportReviewSection Section, string Group);

    private static IReadOnlyList<ImportReviewLayoutRow<Card>> Build(
        IEnumerable<Card> cards, params ImportReviewSection[] collapsed)
        => ImportReviewLayout.Build(cards, c => c.Section, c => c.Group, collapsed.ToHashSet());

    private static IEnumerable<int> Rendered(IEnumerable<ImportReviewLayoutRow<Card>> rows)
        => rows.Where(r => r.Kind == ImportReviewRowKind.ListItem).SelectMany(r => r.Items).Select(c => c.Id);

    [Fact]
    public void SectionsComeInOrderWithEachCardOnItsOwnRowOnce()
    {
        Card[] cards =
        [
            new(1, ImportReviewSection.Identified, "post:a"),
            new(2, ImportReviewSection.Skipped, "post:a"),
            new(3, ImportReviewSection.Unidentified, "folder:x"),
            new(4, ImportReviewSection.Unavailable, "post:gone"),
            new(5, ImportReviewSection.Identified, "post:a"),
        ];

        var rows = Build(cards);

        Assert.Equal([3, 4, 1, 5, 2], Rendered(rows));
        Assert.All(rows.Where(r => r.Kind == ImportReviewRowKind.ListItem), r => Assert.Single(r.Items));
        Assert.Equal(
            [ImportReviewSection.Unidentified, ImportReviewSection.Unavailable, ImportReviewSection.Identified, ImportReviewSection.Skipped],
            rows.Where(r => r.Kind == ImportReviewRowKind.Section).Select(r => r.Section));
    }

    [Fact]
    public void EmptySectionsAreLeftOut()
    {
        Assert.Empty(Build([]));
        var rows = Build([new(1, ImportReviewSection.Identified, "a")]);
        Assert.Equal([ImportReviewSection.Identified], rows.Where(r => r.Kind == ImportReviewRowKind.Section).Select(r => r.Section));
    }

    [Fact]
    public void ACollapsedSectionKeepsOnlyItsHeaderWithItsCount()
    {
        Card[] cards = [new(1, ImportReviewSection.Skipped, "a"), new(2, ImportReviewSection.Skipped, "b"), new(3, ImportReviewSection.Identified, "a")];

        var rows = Build(cards, ImportReviewSection.Skipped);

        var header = Assert.Single(rows, r => r.Section == ImportReviewSection.Skipped);
        Assert.Equal(ImportReviewRowKind.Section, header.Kind);
        Assert.Equal(2, header.Items.Count);
        Assert.Equal([3], Rendered(rows));
    }

    [Fact]
    public void GroupsKeepInputOrderAndNeverMixPosts()
    {
        var cards = Enumerable.Range(0, 11).Select(i => new Card(i, ImportReviewSection.Identified, i % 2 == 0 ? "a" : "b")).ToArray();

        var rows = Build(cards);

        var groups = rows.Where(r => r.Kind == ImportReviewRowKind.Group).ToArray();
        Assert.Equal(2, groups.Length);
        Assert.All(groups, g => Assert.Single(g.Items.Select(c => c.Group).Distinct()));
        Assert.Equal(cards.Where(c => c.Group == "a").Concat(cards.Where(c => c.Group == "b")).Select(c => c.Id), Rendered(rows));
    }

    [Fact]
    public void ModsAreNotLaidOutHere()
        => Assert.Empty(Build([new(1, ImportReviewSection.Mods, "m")]));
}

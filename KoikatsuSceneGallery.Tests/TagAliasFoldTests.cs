using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class TagAliasFoldTests
{
    private static TagCloudEntry Tag(string name, params string[] postKeys)
        => new(name, null, postKeys.Length, postKeys);

    private static TagCloudProviderGroup Group(params TagCloudEntry[] tags)
        => new("pixiv", tags);

    private static Func<string, string?> Aliases(params (string Alias, string Target)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Alias, p => p.Target, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    [Fact]
    public void AnAliasDisappearsIntoItsTarget()
    {
        var folded = TagAliasFold.Fold(
            [Group(Tag("コイカツ!", "a", "b"), Tag("koikatsu!", "c"))],
            Aliases(("koikatsu!", "コイカツ!")));

        var tag = Assert.Single(folded[0].Tags);
        Assert.Equal("コイカツ!", tag.Name);
        Assert.Equal(["koikatsu!"], tag.MergedFrom);
    }

    [Fact]
    public void SharedArtworksAreCountedOnce()
    {
        // The case that makes summing wrong: people tag a work with both
        // spellings, so the counts overlap.
        var folded = TagAliasFold.Fold(
            [Group(Tag("コイカツ!", "a", "b"), Tag("koikatsu!", "b", "c"))],
            Aliases(("koikatsu!", "コイカツ!")));

        var tag = Assert.Single(folded[0].Tags);
        Assert.Equal(3, tag.Count);
        Assert.Equal(["a", "b", "c"], tag.PostKeys.Order());
    }

    [Fact]
    public void SeveralAliasesFoldIntoTheSameTag()
    {
        var folded = TagAliasFold.Fold(
            [Group(Tag("コイカツ!", "a"), Tag("koikatsu", "b"), Tag("koikatsu!", "c"), Tag("恋活", "d"))],
            Aliases(("koikatsu", "コイカツ!"), ("koikatsu!", "コイカツ!"), ("恋活", "コイカツ!")));

        var tag = Assert.Single(folded[0].Tags);
        Assert.Equal(4, tag.Count);
        Assert.Equal(["koikatsu", "koikatsu!", "恋活"], tag.MergedFrom);
    }

    [Fact]
    public void ATagWithNoAliasIsUntouched()
    {
        // The guard that matters: シーン配布(コイカツ!) shares コイカツ!'s
        // parent and is the largest tag in the library it came from.
        var folded = TagAliasFold.Fold(
            [Group(Tag("コイカツ!", "a"), Tag("シーン配布(コイカツ!)", "b", "c"))],
            Aliases(("koikatsu!", "コイカツ!")));

        Assert.Equal(["コイカツ!", "シーン配布(コイカツ!)"], folded[0].Tags.Select(t => t.Name).Order());
        Assert.All(folded[0].Tags, tag => Assert.Empty(tag.MergedFrom));
    }

    [Fact]
    public void AnAliasOfSomethingNotShownStaysWhereItIs()
    {
        // Folding into an absent tag would make it vanish rather than merge.
        var folded = TagAliasFold.Fold(
            [Group(Tag("koikatsu!", "a"))],
            Aliases(("koikatsu!", "コイカツ!")));

        var tag = Assert.Single(folded[0].Tags);
        Assert.Equal("koikatsu!", tag.Name);
        Assert.Empty(tag.MergedFrom);
    }

    [Fact]
    public void AChainEndsAtTheFinalTarget()
    {
        var folded = TagAliasFold.Fold(
            [Group(Tag("A", "a"), Tag("B", "b"), Tag("C", "c"))],
            Aliases(("C", "B"), ("B", "A")));

        var tag = Assert.Single(folded[0].Tags);
        Assert.Equal("A", tag.Name);
        Assert.Equal(["B", "C"], tag.MergedFrom);
    }

    [Fact]
    public void ACycleLosesNothing()
    {
        // Folding each into the other would leave the group empty, which is
        // the one outcome a merge must never produce.
        var folded = TagAliasFold.Fold(
            [Group(Tag("A", "a"), Tag("B", "b"))],
            Aliases(("A", "B"), ("B", "A")));

        Assert.Equal(["A", "B"], folded[0].Tags.Select(t => t.Name).Order());
    }

    [Fact]
    public void TheMergedTagIsReorderedByItsNewCount()
    {
        // A tag that grows past its neighbour must be drawn larger, which the
        // page derives from position and count.
        var folded = TagAliasFold.Fold(
            [Group(Tag("big", "1", "2", "3"), Tag("small", "a"), Tag("alias", "b", "c", "d"))],
            Aliases(("alias", "small")));

        Assert.Equal(["small", "big"], folded[0].Tags.Select(t => t.Name));
        Assert.Equal(4, folded[0].Tags[0].Count);
    }

    [Fact]
    public void NothingChangesWhenTheProviderClaimsNoAliases()
    {
        var group = Group(Tag("a", "1"), Tag("b", "2"));

        var folded = TagAliasFold.Fold([group], _ => null);

        Assert.Same(group, folded[0]);
    }

    [Fact]
    public void EachPlatformFoldsSeparately()
    {
        var folded = TagAliasFold.Fold(
            [
                new TagCloudProviderGroup("pixiv", [Tag("コイカツ!", "a"), Tag("koikatsu!", "b")]),
                new TagCloudProviderGroup("bepisdb", [Tag("koikatsu!", "c")]),
            ],
            Aliases(("koikatsu!", "コイカツ!")));

        Assert.Single(folded[0].Tags);
        // bepisdb has no コイカツ! of its own, so its tag is left alone.
        Assert.Equal("koikatsu!", Assert.Single(folded[1].Tags).Name);
    }
}

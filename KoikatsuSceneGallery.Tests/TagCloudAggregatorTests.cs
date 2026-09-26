using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

public class TagCloudAggregatorTests
{
    private static PostMetadataDocument Doc(
        string provider, string artwork, params (string Name, string? Translated)[] tags) =>
        new(PostMetadataDocument.CurrentSchemaVersion, provider, artwork, "author", "a1", "title", null, 0,
            [.. tags.Select(t => new PostMetadataTag(t.Name, t.Translated))], DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheSameArtworkUnderTwoAuthorFoldersCountsOnce()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("cute", null)),
            Doc("pixiv", "1", ("cute", null)),
        ]);

        Assert.Equal(1, Assert.Single(Assert.Single(groups).Tags).Count);
    }

    [Fact]
    public void PlatformsAreKeptApart()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("cute", null)),
            Doc("bepisdb", "KK_2", ("cute", null)),
        ]);

        Assert.Equal(["bepisdb", "pixiv"], groups.Select(g => g.ProviderId));
        Assert.All(groups, g => Assert.Equal(1, Assert.Single(g.Tags).Count));
    }

    [Fact]
    public void APlatformWithNoTagsProducesNoGroupAtAll()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("cute", null)),
            Doc("fanbox", "2"),
        ]);

        Assert.Equal("pixiv", Assert.Single(groups).ProviderId);
    }

    [Fact]
    public void TagNamesMergeAcrossCaseAndPadding()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("Cute", null)),
            Doc("pixiv", "2", ("  cute ", null)),
        ]);

        Assert.Equal(2, Assert.Single(Assert.Single(groups).Tags).Count);
    }

    [Fact]
    public void TheCommonestTranslationWins()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("kawaii", "cute")),
            Doc("pixiv", "2", ("kawaii", "cute")),
            Doc("pixiv", "3", ("kawaii", "adorable")),
        ]);

        Assert.Equal("cute", Assert.Single(Assert.Single(groups).Tags).TranslatedName);
    }

    [Fact]
    public void ATagSeenOnlyWithoutATranslationHasNone()
    {
        var groups = TagCloudAggregator.Aggregate([Doc("pixiv", "1", ("kawaii", null))]);

        var tag = Assert.Single(Assert.Single(groups).Tags);
        Assert.Null(tag.TranslatedName);
        Assert.Equal("kawaii", tag.Display);
    }

    [Fact]
    public void TagsAreOrderedByCountThenName()
    {
        var groups = TagCloudAggregator.Aggregate([
            Doc("pixiv", "1", ("rare", null), ("b", null), ("a", null)),
            Doc("pixiv", "2", ("b", null), ("a", null)),
        ]);

        Assert.Equal(["a", "b", "rare"], Assert.Single(groups).Tags.Select(t => t.Name));
    }

    [Fact]
    public void TruncationKeepsTheTopTags()
    {
        var docs = Enumerable.Range(0, 5)
            .SelectMany(i => Enumerable.Range(0, i + 1).Select(j => Doc("pixiv", $"{i}-{j}", ($"tag{i}", null))))
            .ToArray();

        var tags = Assert.Single(TagCloudAggregator.Aggregate(docs, maxPerProvider: 2)).Tags;

        Assert.Equal(["tag4", "tag3"], tags.Select(t => t.Name));
    }

    [Fact]
    public void PostKeysMatchTheKeyTheGridGivesTheSameCard()
    {
        var groups = TagCloudAggregator.Aggregate([Doc("pixiv", "98765", ("cute", null))]);

        var expected = GalleryGrouping.GetKey(@"C:\lib\art (98765)\98765_p0.png", "pixiv", "98765");
        Assert.Equal(expected, Assert.Single(Assert.Single(groups).Tags).PostKeys.Single());
    }

    [Fact]
    public void BlankTagNamesAreDropped()
    {
        var groups = TagCloudAggregator.Aggregate([Doc("pixiv", "1", ("   ", null))]);

        Assert.Empty(groups);
    }

    [Fact]
    public void FontSizeSpansTheWholeRange()
    {
        Assert.Equal(14, TagCloudAggregator.FontSize(1, 1, 100));
        Assert.Equal(48, TagCloudAggregator.FontSize(100, 1, 100));
    }

    [Fact]
    public void FontSizeOfAUniformCorpusIsTheMidpoint()
        => Assert.Equal(31, TagCloudAggregator.FontSize(5, 5, 5));

    [Fact]
    public void FontSizeNeverDecreasesAsCountRises()
    {
        var sizes = Enumerable.Range(1, 100).Select(c => TagCloudAggregator.FontSize(c, 1, 100)).ToArray();

        Assert.Equal(sizes, sizes.Order());
    }

    [Fact]
    public void FontSizeStaysInsideTheRangeForAnOutOfRangeCount()
    {
        Assert.InRange(TagCloudAggregator.FontSize(0, 1, 100), 14, 48);
        Assert.InRange(TagCloudAggregator.FontSize(500, 1, 100), 14, 48);
    }
}

/// <summary>
/// The tag page matches sidecar ids against the grid's grouping key, so the
/// key a padded BepisDB file name produces has to be the canonical one the
/// plugin and the sidecar both use.
/// </summary>
public class BepisDbGroupKeyTests
{
    [Theory]
    [InlineData("KKSCENE_078928", "KKSCENE_78928")]
    [InlineData("KKSCENE_78928", "KKSCENE_78928")]
    [InlineData("KKCLOTHING_000099", "KKCLOTHING_99")]
    [InlineData("KK_0", "KK_0")]
    [InlineData("KK_000", "KK_0")]
    public void PaddingIsStrippedFromTheCanonicalId(string raw, string expected)
        => Assert.Equal(expected, FilenameLinkParser.CanonicalBepisDbId(raw));

    [Fact]
    public void AnIdWithNoSeparatorIsLeftAlone()
        => Assert.Equal("KKSCENE", FilenameLinkParser.CanonicalBepisDbId("KKSCENE"));

    [Fact]
    public void APaddedFileNameGroupsUnderTheSameKeyAsTheSidecar()
    {
        var fromFile = GalleryGrouping.GetKey(@"C:\lib\KKSCENE_067591.png");
        var fromSidecar = GalleryGrouping.GetKey(string.Empty, "bepisdb", "KKSCENE_67591");

        Assert.Equal(fromSidecar, fromFile);
    }

    [Fact]
    public void ThePluginParsedPathAgreesWithTheFileNameFallback()
    {
        // With the plugin installed the id arrives already stripped; without it
        // the fallback derives it. Both must land on one key.
        var withPlugin = GalleryGrouping.GetKey(@"C:\lib\KKSCENE_067591.png", "bepisdb", "KKSCENE_67591");
        var withoutPlugin = GalleryGrouping.GetKey(@"C:\lib\KKSCENE_067591.png");

        Assert.Equal(withPlugin, withoutPlugin);
    }
}

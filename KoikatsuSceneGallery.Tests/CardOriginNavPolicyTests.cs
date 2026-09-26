using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class CardOriginNavPolicyTests
{
    // The library this was written against: both carry tags, FANBOX carries
    // none, and all three have authors.
    private static readonly string[] WithTags = ["bepisdb", "pixiv"];
    private static readonly string[] WithAuthors = ["bepisdb", "pixiv", "fanbox"];

    [Fact]
    public void APlatformWithNoTagsIsNotOfferedTheTagPage()
    {
        // The entry used to appear for every platform, so choosing FANBOX led
        // to an empty cloud — which reads as a fault rather than as an absence.
        Assert.False(CardOriginNavPolicy.ShowTags(CardOriginSelection.For("fanbox"), WithTags));
        Assert.True(CardOriginNavPolicy.ShowTags(CardOriginSelection.For("pixiv"), WithTags));
    }

    [Fact]
    public void TheSamePlatformMayOfferAuthorsWithoutTags()
    {
        // The two pages are gated independently: FANBOX has authors to list and
        // no tags to browse.
        Assert.True(CardOriginNavPolicy.ShowAuthors(CardOriginSelection.For("fanbox"), WithAuthors));
        Assert.False(CardOriginNavPolicy.ShowTags(CardOriginSelection.For("fanbox"), WithTags));
    }

    [Fact]
    public void AllKeepsBothPagesWhileAnyPlatformHasData()
    {
        // Under "all" the pages keep their own platform picker, so they stay
        // reachable.
        Assert.True(CardOriginNavPolicy.ShowTags(CardOriginSelection.All, WithTags));
        Assert.True(CardOriginNavPolicy.ShowAuthors(CardOriginSelection.All, WithAuthors));
    }

    [Fact]
    public void NoDataAnywhereHidesBothPages()
    {
        Assert.False(CardOriginNavPolicy.ShowTags(CardOriginSelection.All, []));
        Assert.False(CardOriginNavPolicy.ShowAuthors(CardOriginSelection.All, []));
        Assert.False(CardOriginNavPolicy.ShowTags(CardOriginSelection.For("pixiv"), []));
    }

    [Fact]
    public void LocalNeverOffersEither()
    {
        // Both pages list remote data by definition, whatever is installed.
        Assert.False(CardOriginNavPolicy.ShowTags(CardOriginSelection.Local, WithTags));
        Assert.False(CardOriginNavPolicy.ShowAuthors(CardOriginSelection.Local, WithAuthors));
    }

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.True(CardOriginNavPolicy.ShowTags(CardOriginSelection.For("PIXIV"), WithTags));

    [Fact]
    public void LocalSourcesHideWhileARemotePlatformIsSelected()
    {
        Assert.True(CardOriginNavPolicy.ShowLocalSources(CardOriginSelection.All));
        Assert.True(CardOriginNavPolicy.ShowLocalSources(CardOriginSelection.Local));
        Assert.False(CardOriginNavPolicy.ShowLocalSources(CardOriginSelection.For("pixiv")));
    }
}

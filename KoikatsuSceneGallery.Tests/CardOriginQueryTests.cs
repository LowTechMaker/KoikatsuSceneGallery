using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class CardOriginQueryTests
{
    [Fact]
    public void AllShowsEveryCard()
    {
        foreach (var providerId in new string?[] { null, "", "pixiv", "local", "bepisdb" })
            Assert.True(CardOriginQuery.Passes(providerId, CardOriginSelection.All));
    }

    [Fact]
    public void APlatformShowsOnlyItsOwnCards()
    {
        var pixiv = CardOriginSelection.For("pixiv");

        Assert.True(CardOriginQuery.Passes("pixiv", pixiv));
        Assert.False(CardOriginQuery.Passes("bepisdb", pixiv));
        Assert.False(CardOriginQuery.Passes("local", pixiv));
    }

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.True(CardOriginQuery.Passes("PIXIV", CardOriginSelection.For("pixiv")));

    [Fact]
    public void LocalIsAPlatformLikeAnyOther()
    {
        Assert.True(CardOriginQuery.Passes("local", CardOriginSelection.Local));
        Assert.False(CardOriginQuery.Passes("pixiv", CardOriginSelection.Local));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ACardWithNoPlatformAppearsOnlyUnderAll(string? providerId)
    {
        // Its author never resolved, so there is no platform it honestly
        // belongs to. The previous switch let it through as "not local".
        Assert.True(CardOriginQuery.Passes(providerId, CardOriginSelection.All));
        Assert.False(CardOriginQuery.Passes(providerId, CardOriginSelection.For("pixiv")));
        Assert.False(CardOriginQuery.Passes(providerId, CardOriginSelection.Local));
    }

    [Fact]
    public void BlankSelectionMeansAll()
    {
        Assert.True(CardOriginSelection.For(null).IsAll);
        Assert.True(CardOriginSelection.For("   ").IsAll);
    }

    [Fact]
    public void AStoredPlatformSurvivesARoundTrip()
    {
        var parsed = CardOriginSelection.Parse(CardOriginSelection.For("bepisdb").ToConfigValue());

        Assert.Equal("bepisdb", parsed.ProviderId);
    }

    [Theory]
    // Written by the three-way switch this replaced.
    [InlineData("All", null)]
    // No single platform means "everything except one", so it widens rather
    // than narrowing — the safe direction for an invisible migration.
    [InlineData("ExcludeLocal", null)]
    [InlineData("LocalOnly", "local")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void OldStoredValuesStillRead(string? stored, string? expected)
        => Assert.Equal(expected, CardOriginSelection.Parse(stored).ProviderId);
}

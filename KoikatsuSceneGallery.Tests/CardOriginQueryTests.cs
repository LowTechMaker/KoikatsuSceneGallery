using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardOriginQueryTests
{
    [Fact]
    public void Passes_CoversEveryOriginAndFilterCombination()
    {
        (string? ProviderId, CardOriginFilter Filter, bool Expected)[] cases =
        [
            // All: everything passes, whatever the origin.
            (null, CardOriginFilter.All, true),
            ("pixiv", CardOriginFilter.All, true),
            ("local", CardOriginFilter.All, true),
            // ExcludeLocal: cards without an author stay visible; only local is hidden.
            (null, CardOriginFilter.ExcludeLocal, true),
            ("", CardOriginFilter.ExcludeLocal, true),
            ("pixiv", CardOriginFilter.ExcludeLocal, true),
            ("local", CardOriginFilter.ExcludeLocal, false),
            ("LOCAL", CardOriginFilter.ExcludeLocal, false),
            // LocalOnly: the mirror image.
            (null, CardOriginFilter.LocalOnly, false),
            ("pixiv", CardOriginFilter.LocalOnly, false),
            ("local", CardOriginFilter.LocalOnly, true),
            ("LOCAL", CardOriginFilter.LocalOnly, true),
        ];

        foreach (var (providerId, filter, expected) in cases)
        {
            Assert.Equal(
                expected,
                CardOriginQuery.Passes(providerId, filter));
        }
    }

    [Fact]
    public void Passes_PartitionsCardsBetweenTheTwoNarrowingFilters()
    {
        foreach (var providerId in new string?[] { null, "", "pixiv", "bepisdb", "local" })
        {
            Assert.NotEqual(
                CardOriginQuery.Passes(providerId, CardOriginFilter.ExcludeLocal),
                CardOriginQuery.Passes(providerId, CardOriginFilter.LocalOnly));
        }
    }
}

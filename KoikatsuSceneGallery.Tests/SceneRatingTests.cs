using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class SceneRatingTests
{
    private static readonly HashSet<string> R18 = new(["R-18"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> R18G = new(["R-18G"], StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData(@"C:\lib\G\author\a.png", 0)]
    [InlineData(@"C:\lib\R-18\author\a.png", 1)]
    [InlineData(@"C:\lib\r-18g\author\a.png", 2)]
    [InlineData(@"C:\lib\R-18\R-18G\a.png", 2)]
    [InlineData(@"C:\lib\author\R-18.png", 0)]
    public void FolderRatingFollowsTheRatingFolderInThePath(string path, int expected)
        => Assert.Equal(expected, SceneRating.FromFolders(path, R18, R18G));

    [Theory]
    // Platform source: the post's rating wins, the folder fills in when there is none.
    [InlineData(1, 2, null, false, 2)]
    [InlineData(1, null, null, false, 1)]
    [InlineData(1, 0, 2, false, 0)]
    // User source: their rating wins, an unrated card falls back to the platform rule.
    [InlineData(0, 1, 2, true, 2)]
    [InlineData(0, 1, null, true, 1)]
    [InlineData(2, null, null, true, 2)]
    // Out-of-range values are ignored rather than trusted.
    [InlineData(1, 5, 7, true, 1)]
    public void ResolvePicksTheSelectedSourceWithFallbacks(
        int folder, int? platform, int? user, bool useUser, int expected)
        => Assert.Equal(expected, SceneRating.Resolve(folder, platform, user, useUser));

    [Theory]
    // The folder still says it: the folder wins, as it always has.
    [InlineData(1, null, 1)]
    [InlineData(2, null, 2)]
    [InlineData(1, 2, 2)]
    [InlineData(2, 1, 2)]
    [InlineData(1, 0, 1)]
    // The folder says G (no rating folder, as in the new layout): the filed rating fills in.
    [InlineData(0, 1, 1)]
    [InlineData(0, 2, 2)]
    [InlineData(0, 0, 0)]
    [InlineData(0, null, 0)]
    // Out of range is not a rating.
    [InlineData(0, 5, 0)]
    [InlineData(0, -1, 0)]
    public void TheFiledRatingFillsInWhenTheFolderSaysGeneral(int folder, int? filed, int expected)
        => Assert.Equal(expected, SceneRating.FolderOrFiled(folder, filed));

    [Theory]
    // The three-level layout has no rating folder anywhere in the path.
    [InlineData("pixiv", "Author (42)", "Title (7)")]
    [InlineData("Unknown", "!unrecognized", "")]
    [InlineData("pixiv", "Author (42)", "!unrecognized")]
    public void ACardInTheNewLayoutReadsAsGeneralFromItsFolders(string provider, string author, string artwork)
    {
        var path = Path.Combine("lib", "Organized", provider, author, artwork, "a.png");
        Assert.Equal(SceneRating.General, SceneRating.FromFolders(path, R18, R18G));
    }

    [Fact]
    public void ARatedCardFiledInTheNewLayoutStillReadsAsRated()
    {
        // The whole chain for a local or fetch-failed card: no post rating, a
        // folder that says nothing, a filed rating that does.
        var path = Path.Combine("lib", "Organized", "local", "Me (local-1)", "a.png");
        var folder = SceneRating.FolderOrFiled(SceneRating.FromFolders(path, R18, R18G), SceneRating.R18);

        Assert.Equal(SceneRating.R18, SceneRating.Resolve(folder, platformRating: null, userRating: null, useUserRatings: false));
    }
}

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
}

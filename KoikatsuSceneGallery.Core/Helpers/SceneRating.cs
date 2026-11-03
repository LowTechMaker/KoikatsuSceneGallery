namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The rules that turn a scene card's three rating sources into the one rating
/// the gallery filters and labels by. Values use the platform scale:
/// 0 = G, 1 = R-18, 2 = R-18G.
/// </summary>
internal static class SceneRating
{
    public const int General = 0;
    public const int R18 = 1;
    public const int R18G = 2;

    /// <summary>
    /// The rating implied by the rating folder the card sits under. R-18G wins
    /// over R-18 when both names appear in the path, and a path under neither
    /// is G.
    /// </summary>
    public static int FromFolders(string filePath, IReadOnlySet<string> r18Folders, IReadOnlySet<string> r18GFolders)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return General;

        var rating = General;
        foreach (var segment in directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (r18GFolders.Contains(segment)) return R18G;
            if (r18Folders.Contains(segment)) rating = R18;
        }
        return rating;
    }

    /// <summary>
    /// The rating to show. "Platform" means the fetched post's rating, or the
    /// folder when no post data exists (local cards, failed fetches). With the
    /// user's ratings selected, a card they have not rated yet falls back to
    /// that platform rating, so switching sources never hides unrated cards.
    /// </summary>
    public static int Resolve(int folderRating, int? platformRating, int? userRating, bool useUserRatings)
    {
        var platform = platformRating is >= General and <= R18G ? platformRating.Value : folderRating;
        return useUserRatings && userRating is >= General and <= R18G ? userRating.Value : platform;
    }
}

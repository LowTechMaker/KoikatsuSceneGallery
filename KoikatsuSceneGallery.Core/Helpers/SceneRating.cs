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
    /// The card's folder rating once its filed rating is taken into account:
    /// the folder's when it says R-18 or R-18G, otherwise the rating the card
    /// was filed under (<c>CardState.FiledRating</c>), otherwise G.
    /// </summary>
    /// <remarks>
    /// The simpler library layout has no rating folders, so a local or
    /// fetch-failed R-18 card moved into it would read as G from its folder.
    /// Its filed rating is recorded before it leaves the rating folder; this
    /// is where that record takes the folder's place. A folder that still says
    /// a rating keeps winning, so a library that has not moved reads exactly
    /// as before.
    /// </remarks>
    public static int FolderOrFiled(int folderRating, int? filedRating)
        // The higher of the two: showing an R-18G card as R-18 is the mistake to avoid.
        => filedRating is >= General and <= R18G ? Math.Max(folderRating, filedRating.Value) : folderRating;

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

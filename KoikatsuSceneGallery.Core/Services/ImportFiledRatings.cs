using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Which freshly imported cards get their rating filed as data
/// (<see cref="CardState.FiledRating"/>).
/// </summary>
/// <remarks>
/// New folders have no rating folder, so an imported card's rating is only
/// visible from its folder until a post sidecar says otherwise, and for a
/// local or fetch-failed card never. Every committed card rated above G is
/// filed; G is the default and needs no record.
/// </remarks>
internal static class ImportFiledRatings
{
    /// <param name="committedPaths">The destinations the transaction committed.</param>
    /// <param name="items">Each item's planned destination and rating (0 = G, 1 = R-18, 2 = R-18G).</param>
    public static List<(string Path, int Rating)> ForCommitted(
        IReadOnlyList<string> committedPaths,
        IEnumerable<(string? Destination, int Rating)> items)
    {
        var ratingByPath = new Dictionary<string, int>(PathComparison.Comparer);
        foreach (var (destination, rating) in items)
        {
            if (!string.IsNullOrWhiteSpace(destination))
                ratingByPath[Path.GetFullPath(destination)] = rating;
        }

        var filed = new List<(string, int)>();
        foreach (var path in committedPaths)
        {
            if (ratingByPath.TryGetValue(Path.GetFullPath(path), out var rating) && rating is SceneRating.R18 or SceneRating.R18G)
                filed.Add((path, rating));
        }
        return filed;
    }
}

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Works out each scene card's rating from the source chosen in settings:
/// the platform's (post sidecar, else the rating folder) or the user's own
/// (<see cref="UserCardRatingStore"/>, else the platform's).
/// </summary>
/// <remarks>
/// The platform ratings are read once per library load into a post-key map,
/// keyed through <see cref="CardGroupKey"/> — the same identity the grid groups
/// by and the tag page matches with — so no card is ever hashed or its sidecar
/// read one by one. User ratings are looked up by path; a card moved since it
/// was rated is only recognised by its hash, which the rating and detail pages
/// compute, and they update the stored path.
/// </remarks>
internal sealed class SceneRatingResolver(PluginService plugins, UserCardRatingStore userRatings, IAppLogger logger)
{
    private readonly PostMetadataStore _store = new();
    private IReadOnlyDictionary<string, int> _platform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public bool UseUserRatings { get; set; }

    public UserCardRatingStore UserRatings => userRatings;

    /// <summary>Reads the sidecars of <paramref name="cards"/> and the user rating files of <paramref name="roots"/>. Off the UI thread.</summary>
    public async Task ReloadAsync(IReadOnlyList<SceneCard> cards, IReadOnlyList<string> roots, CancellationToken token)
    {
        var map = await Task.Run(() =>
        {
            userRatings.EnsureLoaded(roots);
            var directories = PostSidecarLocator.AuthorDirectories(cards, logger);
            var documents = PostSidecarLocator.ReadAll(_store, directories, logger, "SceneRating.ReadSidecars", token);
            var ratings = new Dictionary<string, (int Rating, DateTimeOffset FetchedAt)>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in documents)
            {
                // A post can have a sidecar in several author folders (one per
                // rating folder); the newest fetch speaks for the platform.
                var key = GalleryGrouping.GetKey(string.Empty, document.ProviderId, document.ArtworkId);
                if (!ratings.TryGetValue(key, out var known) || document.FetchedAt > known.FetchedAt)
                    ratings[key] = (document.Rating, document.FetchedAt);
            }
            return ratings.ToDictionary(p => p.Key, p => p.Value.Rating, StringComparer.OrdinalIgnoreCase);
        }, token);
        _platform = map;
    }

    public int Resolve(SceneCard card)
    {
        int? platform = _platform.Count > 0 && _platform.TryGetValue(CardGroupKey.For(card, plugins, logger), out var rating)
            ? rating
            : null;
        int? user = UseUserRatings ? userRatings.TryGetByPath(card.FilePath) : null;
        return SceneRating.Resolve(card.FolderRating, platform, user, UseUserRatings);
    }
}

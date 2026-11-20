using System.Text.Json;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>What a post sidecar says about the post, beyond its rating: for the home page's tiles.</summary>
internal sealed record ScenePostInfo(string? Title, IReadOnlyList<(string Name, string? Translated)> Tags);

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
///
/// The same read keeps each post's title and tags, which the home page shows
/// instead of file names: one pass over the sidecars per load, off the UI
/// thread, and a dictionary lookup per tile afterwards.
/// </remarks>
internal sealed class SceneRatingResolver(PluginService plugins, UserCardRatingStore userRatings, IAppLogger logger)
{
    private readonly PostMetadataStore _store = new();
    private IReadOnlyDictionary<string, int> _platform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, ScenePostInfo> _posts = new Dictionary<string, ScenePostInfo>(StringComparer.OrdinalIgnoreCase);

    public bool UseUserRatings { get; set; }

    public UserCardRatingStore UserRatings => userRatings;

    /// <summary>Raised, on a background thread, after the sidecars were read again.</summary>
    public event Action? PostsReloaded;

    private int _generation;

    /// <summary>
    /// Reads the sidecars of <paramref name="cards"/> and the user rating files of
    /// <paramref name="roots"/>. Off the UI thread. Returns the number of sidecars
    /// read, or null when a later reload started meanwhile: the start reads the
    /// ratings of the cached cards first and again after the scan if it found new
    /// cards, and the earlier, smaller read must never overwrite the later one.
    /// </summary>
    public async Task<int?> ReloadAsync(IReadOnlyList<SceneCard> cards, IReadOnlyList<string> roots, CancellationToken token)
    {
        var generation = Interlocked.Increment(ref _generation);
        var (ratings, posts) = await Task.Run(() =>
        {
            userRatings.EnsureLoaded(roots);
            var directories = PostSidecarLocator.AuthorDirectories(cards, logger);
            var documents = PostSidecarLocator.ReadAll(_store, directories, logger, "SceneRating.ReadSidecars", token);
            var newest = new Dictionary<string, PostMetadataDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var document in documents)
            {
                // A post can have a sidecar in several author folders (one per
                // rating folder); the newest fetch speaks for the platform.
                var key = GalleryGrouping.GetKey(string.Empty, document.ProviderId, document.ArtworkId);
                if (!newest.TryGetValue(key, out var known) || document.FetchedAt > known.FetchedAt)
                    newest[key] = document;
            }
            return (
                newest.ToDictionary(p => p.Key, p => p.Value.Rating, StringComparer.OrdinalIgnoreCase),
                newest.ToDictionary(p => p.Key,
                    p => new ScenePostInfo(p.Value.Title,
                        [.. (p.Value.Tags ?? []).Select(t => (t.Name, t.TranslatedName))]),
                    StringComparer.OrdinalIgnoreCase));
        }, token);
        if (generation != Volatile.Read(ref _generation)) return null;
        _platform = ratings;
        _posts = posts;
        HasPosts = true;
        PostsReloaded?.Invoke();
        // For the next start (LoadSnapshot): written off the caller's thread.
        _ = Task.Run(() => SaveSnapshot(ratings, posts));
        return posts.Count;
    }

    // ------------------------------------------------------------ the start snapshot

    /// <summary>
    /// Whether ratings, titles and tags are known: from this run's read of the
    /// sidecars or from the snapshot of the last one.
    /// </summary>
    public bool HasPosts { get; private set; }

    private const string SnapshotFileName = "scene_posts.json";
    private const int SnapshotVersion = 1;
    private static string SnapshotPath => Path.Combine(AppPaths.LocalFolder, SnapshotFileName);

    private sealed record SnapshotPost(int R, string? T, List<string?[]>? G);
    private sealed record Snapshot(int V, Dictionary<string, SnapshotPost> Posts);

    /// <summary>
    /// Takes the ratings, titles and tags the last run read from the sidecars,
    /// so a start can label and filter its cached cards at once instead of
    /// waiting for every sidecar (about 12,000 of them, 3-15 s, measured in
    /// startup_timing.log). The read still runs after it and corrects whatever
    /// changed since. Off the UI thread. Returns whether a snapshot was taken.
    /// </summary>
    public bool LoadSnapshot(IReadOnlyList<string> roots)
    {
        if (HasPosts) return true;
        try
        {
            userRatings.EnsureLoaded(roots);
            if (!File.Exists(SnapshotPath)) return false;
            using var stream = File.OpenRead(SnapshotPath);
            var snapshot = JsonSerializer.Deserialize<Snapshot>(stream);
            if (snapshot is not { V: SnapshotVersion, Posts: { } stored }) return false;
            var ratings = new Dictionary<string, int>(stored.Count, StringComparer.OrdinalIgnoreCase);
            var posts = new Dictionary<string, ScenePostInfo>(stored.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, post) in stored)
            {
                ratings[key] = post.R;
                posts[key] = new ScenePostInfo(post.T,
                    [.. (post.G ?? []).Where(tag => tag.Length > 0 && tag[0] is not null).Select(tag => (tag[0]!, tag.Length > 1 ? tag[1] : null))]);
            }
            // A read that already finished this run is newer; keep it.
            if (HasPosts) return true;
            _platform = ratings;
            _posts = posts;
            HasPosts = true;
            PostsReloaded?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            // A damaged snapshot only costs the wait it was there to save.
            logger.LogError("SceneRating.LoadSnapshot", ex);
            return false;
        }
    }

    private void SaveSnapshot(IReadOnlyDictionary<string, int> ratings, IReadOnlyDictionary<string, ScenePostInfo> posts)
    {
        try
        {
            var stored = new Dictionary<string, SnapshotPost>(ratings.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, rating) in ratings)
            {
                var info = posts.GetValueOrDefault(key);
                stored[key] = new SnapshotPost(rating, info?.Title,
                    info is null ? null : [.. info.Tags.Select(tag => new[] { tag.Name, tag.Translated })]);
            }
            var temp = SnapshotPath + ".tmp";
            using (var stream = File.Create(temp))
                JsonSerializer.Serialize(stream, new Snapshot(SnapshotVersion, stored));
            File.Move(temp, SnapshotPath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogError("SceneRating.SaveSnapshot", ex);
        }
    }

    public int Resolve(SceneCard card)
    {
        int? platform = _platform.Count > 0 && _platform.TryGetValue(CardGroupKey.For(card, plugins, logger), out var rating)
            ? rating
            : null;
        int? user = UseUserRatings ? userRatings.TryGetByPath(card.FilePath) : null;
        return SceneRating.Resolve(card.FolderRating, platform, user, UseUserRatings);
    }

    /// <summary>The card's post key (<see cref="CardGroupKey"/>): <c>post:…</c> when it belongs to a post.</summary>
    public string PostKeyOf(CardBase card) => CardGroupKey.For(card, plugins, logger);

    /// <summary>The title and tags of the card's post, when a sidecar has them.</summary>
    public ScenePostInfo? PostInfo(string postKey) => _posts.GetValueOrDefault(postKey);

    /// <summary>
    /// The artwork id the card's folder name parses to through its author's
    /// provider, or null: the fallback title is in that folder's name.
    /// </summary>
    public string? FolderArtworkId(CardBase card)
    {
        var providerId = (card as IAuthorOwner)?.Author?.Key.ProviderId;
        var provider = plugins.ImportProviders.FirstOrDefault(p => p.ProviderId == providerId);
        try
        {
            return provider?.TryParseArtworkFolderName(Path.GetFileName(Path.GetDirectoryName(card.FilePath)) ?? "")?.Id;
        }
        catch (Exception ex)
        {
            logger.LogError("SceneRating.FolderArtworkId", ex, card.FilePath);
            return null;
        }
    }
}

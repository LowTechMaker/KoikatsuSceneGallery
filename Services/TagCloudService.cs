using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Builds the per-platform tag cloud from the sidecars that travel with the
/// library, and answers "which cards carry this tag".
/// </summary>
/// <remarks>
/// Author folders are inferred from the cards already in memory rather than by
/// walking the library tree. The per-author scan that
/// <see cref="AuthorPostService"/> performs re-walks every root for each
/// author, which is fine for one author page and unusable for a whole-library
/// cloud.
/// </remarks>
internal sealed class TagCloudService(LibraryRegistry libraries, PluginService plugins, IAppLogger logger)
{
    private readonly PostMetadataStore _store = new();

    /// <summary>The platforms with a plugin installed right now.</summary>
    public IReadOnlyList<string> InstalledProviderIds() =>
        [.. plugins.ImportProviders.Select(provider => provider.ProviderId)];

    /// <summary>Whether the tag feature has any platform to show at all.</summary>
    public bool HasAnyPlatform => PlatformAvailability.HasAny(InstalledProviderIds());

    /// <summary>The last built cloud, or null before the first build.</summary>
    public IReadOnlyList<TagCloudProviderGroup>? Cloud { get; private set; }

    /// <summary>Raised once a build finishes and <see cref="Cloud"/> changes.</summary>
    public event Action? CloudChanged;

    /// <summary>
    /// The platforms that actually carry tags.
    /// </summary>
    /// <remarks>
    /// Empty until the first build. That is the honest answer rather than a
    /// guess: whether a platform has tags is a fact about the sidecars on disk,
    /// not about which plugin is installed — FANBOX's plugin is installed here
    /// and its artworks carry none.
    /// </remarks>
    public IReadOnlyList<string> ProvidersWithTags =>
        Cloud is null ? [] : [.. Cloud.Where(group => group.Tags.Count > 0).Select(group => group.ProviderId)];

    /// <summary>
    /// The platforms the tag page should be offered for.
    /// </summary>
    /// <remarks>
    /// Before the first build there is no way to know which platforms carry
    /// tags, so every installed one is assumed to. Erring towards offering the
    /// page means at worst a moment of an entry that then goes away; erring the
    /// other way would hide a page that is in fact available, which the user
    /// has no way to discover.
    /// </remarks>
    public IReadOnlyList<string> PlatformsOfferingTags =>
        Cloud is null ? PlatformAvailability.Online(InstalledProviderIds()) : ProvidersWithTags;

    private Task<IReadOnlyList<TagCloudProviderGroup>>? _building;

    /// <summary>
    /// Reads every sidecar and builds the cloud, or joins the build already
    /// under way.
    /// </summary>
    /// <remarks>
    /// Joining matters beyond saving a second pass over the disk. The window
    /// warms the cloud at startup while the tag page may ask for it too, and
    /// the page treats a cloud it did not build as news — two overlapping
    /// builds made it announce "tag data updated" when nothing had changed.
    /// Called on the UI thread, like every caller today.
    /// </remarks>
    public Task<IReadOnlyList<TagCloudProviderGroup>> BuildAsync(CancellationToken token = default)
    {
        if (_building is { IsCompleted: false } running) return running;
        return _building = BuildCoreAsync(token);
    }

    private async Task<IReadOnlyList<TagCloudProviderGroup>> BuildCoreAsync(CancellationToken token)
    {
        var directories = AuthorDirectories();

        // No directories means no cards have been scanned yet, not that no tags
        // exist: the author folders are derived from the paths of the cards in
        // memory. Caching the empty result of that would answer "this platform
        // has no tags" for the rest of the session and hide the page for good.
        if (directories.Count == 0) return Cloud ?? [];

        var cloud = await Task.Run(() =>
        {
            var documents = new List<PostMetadataDocument>();
            foreach (var directory in directories)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    documents.AddRange(_store.ReadAll(directory));
                }
                catch (Exception ex)
                {
                    logger.LogError("TagCloud.ReadSidecars", ex, directory);
                }
            }
            var all = TagCloudAggregator.Aggregate(documents);
            // Sidecars outlive their plugin, so a platform whose plugin is gone
            // would otherwise keep offering its tags.
            return PlatformAvailability.RestrictToInstalled(all, InstalledProviderIds());
        }, token);

        Cloud = cloud;
        CloudChanged?.Invoke();
        return cloud;
    }

    /// <summary>
    /// The cards carrying <paramref name="tag"/>, grouped by the library each
    /// belongs to so a caller can show one browser per kind.
    /// </summary>
    public IReadOnlyDictionary<LibraryKind, IReadOnlyList<CardBase>> CardsFor(TagCloudEntry tag)
    {
        var wanted = tag.PostKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<LibraryKind, IReadOnlyList<CardBase>>();
        foreach (var library in libraries.All)
        {
            var matches = library.Cards
                .Where(card => wanted.Contains(KeyOf(card)))
                .DistinctBy(card => card.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (matches.Length > 0) result[library.Kind] = matches;
        }
        return result;
    }

    /// <summary>
    /// The card's grouping key, through the very same helper the grid uses, so
    /// the two sides cannot disagree.
    /// </summary>
    private string KeyOf(CardBase card) => CardGroupKey.For(card, plugins, logger);

    /// <summary>
    /// Every directory holding sidecars, found from the cards themselves. A
    /// card sits either directly in its author folder or one artwork subfolder
    /// down, so exactly two levels are probed.
    /// </summary>
    private IReadOnlyList<string> AuthorDirectories()
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in libraries.All.SelectMany(library => library.Cards))
        {
            var folder = Path.GetDirectoryName(card.FilePath);
            if (string.IsNullOrEmpty(folder)) continue;
            candidates.Add(folder);
            if (Path.GetDirectoryName(folder) is { Length: > 0 } parent) candidates.Add(parent);
        }

        return [.. candidates.Where(HasSidecars)];
    }

    private bool HasSidecars(string directory)
    {
        try
        {
            return Directory.Exists(Path.Combine(
                directory, PostMetadataStore.MetadataDirectoryName, PostMetadataStore.FetchedDataDirectoryName));
        }
        catch (Exception ex)
        {
            logger.LogError("TagCloud.ProbeDirectory", ex, directory);
            return false;
        }
    }
}

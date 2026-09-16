using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Built-in provider for locally collected cards: files that are not publicly
/// shared, or that a friend passed on privately.
/// </summary>
/// <remarks>
/// It implements the same provider interfaces as an import plugin so a local
/// source becomes an author like any other — with a badge, a detail page, a
/// tab on the authors page, scoped browsing and a resolved import destination
/// — without a single line of provider-specific gallery code.
///
/// Everything it answers comes from the folder name or from
/// <see cref="LocalSourceRegistry"/>, so no call on it ever reaches the
/// network. It is registered in process rather than loaded from a DLL, which
/// is why it can be constructed with app services.
/// </remarks>
public sealed class LocalSourceProvider :
    IPlugin,
    IFolderAuthorProvider,
    ICardImportProvider,
    IImportDestinationProvider
{
    private readonly LocalSourceRegistry _registry;

    public LocalSourceProvider(LocalSourceRegistry registry) => _registry = registry;

    public string Name => "Local sources";

    public string Version => "1.0";

    public string ProviderId => LocalSourceIdentity.ProviderId;

    /// <summary>Folder claimed below the import subfolder, from the user's settings.</summary>
    public string DestinationFolderName => _registry.LocalFolderName;

    /// <summary>
    /// Local cards carry a rating like any other, and the gallery derives R-18
    /// from the destination path, so skipping rating folders here would make
    /// "hide R-18" silently ineffective for them.
    /// </summary>
    public bool UsesRatingFolders => true;

    public void Initialize(IPluginHost host)
    {
        // Registered in process; there is no plugin storage to prepare.
    }

    public ParsedAuthor? TryParseFolderName(string folderName)
        => LocalSourceIdentity.TryParseFolderId(folderName, out var id, out var displayName)
            ? new ParsedAuthor(new AuthorKey(ProviderId, id), displayName)
            : null;

    public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct)
    {
        if (!LocalSourceIdentity.IsLocal(key.ProviderId))
            return Task.FromResult<AuthorInfo?>(null);

        return forceRefresh
            ? RefreshAuthorInfoAsync(key, ct)
            : Task.FromResult(ReadAuthorInfo(key));
    }

    /// <summary>
    /// Empty by design: a local source has no page to open. Callers gate their
    /// "open profile" affordance on AuthorDisplay.HasProfileUrl.
    /// </summary>
    public string GetProfileUrl(AuthorKey key) => "";

    // A local card has no remote artwork identity, and inventing one would
    // pollute artwork grouping, the folder index and sidecar file names. Cards
    // from one source are grouped by their folder instead.
    public ArtworkId? TryParseFilename(string fileName) => null;

    public ArtworkId? TryParseArtworkFolderName(string folderName) => null;

    public ArtworkId? TryParseUrl(string url) => null;

    public Task<ArtworkInfo?> FetchArtworkInfoAsync(
        ArtworkId id,
        CancellationToken ct,
        bool saveToLocalCache = true)
        => Task.FromResult<ArtworkInfo?>(null);

    public string GetArtworkUrl(ArtworkId id) => "";

    private async Task<AuthorInfo?> RefreshAuthorInfoAsync(AuthorKey key, CancellationToken ct)
    {
        // "Refresh" means re-read the disk, which is also how a source folder
        // that appeared after startup becomes resolvable.
        var source = await _registry.ReloadAsync(key.Id, ct).ConfigureAwait(false);
        return source is null
            ? null
            : new AuthorInfo(key, source.DisplayName, source.AvatarPath, GetProfileUrl(key), DateTimeOffset.UtcNow);
    }

    private AuthorInfo? ReadAuthorInfo(AuthorKey key)
        => _registry.Find(key.Id) is { } source
            ? new AuthorInfo(key, source.DisplayName, source.AvatarPath, GetProfileUrl(key), DateTimeOffset.UtcNow)
            : null;
}

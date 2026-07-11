using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Discovers artwork posts for a given author by scanning the local library
/// folder structure for artwork IDs (from subfolder names and filenames),
/// then enriches them with cached metadata when available.
/// </summary>
public sealed class AuthorPostService
{
    private sealed class PostAccumulator
    {
        public required string ProviderId { get; init; }
        public string? Title { get; set; }
        public List<string> FilePaths { get; } = [];
    }

    private readonly IReadOnlyList<ICardImportProvider> _importProviders;
    private readonly IReadOnlyList<IFolderAuthorProvider> _authorProviders;
    private readonly SettingsService _settingsService;

    public AuthorPostService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        SettingsService settingsService)
    {
        _importProviders = importProviders;
        _authorProviders = authorProviders;
        _settingsService = settingsService;
    }

    private ArtworkId? TryParseFilename(string fileName, string providerId)
    {
        var provider = FindProvider(providerId);
        return provider?.TryParseFilename(fileName);
    }

    private ArtworkId? TryParseArtworkFolderName(string folderName, string providerId)
    {
        var provider = FindProvider(providerId);
        return provider?.TryParseArtworkFolderName(folderName);
    }

    private ICardImportProvider? FindProvider(string providerId)
        => _importProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    private IFolderAuthorProvider? FindAuthorProvider(string providerId)
        => _authorProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    public bool CanScanPosts(AuthorKey authorKey)
        => FindAuthorProvider(authorKey.ProviderId) is not null
           && FindProvider(authorKey.ProviderId) is not null;

    /// <summary>
    /// Scans all library roots for folders belonging to <paramref name="authorKey"/>
    /// and returns deduplicated artwork IDs found in subfolder names and filenames.
    /// Each result includes the folder-derived title (if any) and local file count.
    /// </summary>
    public async Task<List<AuthorPost>> ScanAuthorPostsAsync(
        AuthorKey authorKey, CancellationToken ct)
    {
        var authorProvider = FindAuthorProvider(authorKey.ProviderId);
        if (authorProvider is null) return [];

        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var posts = new Dictionary<string, PostAccumulator>(StringComparer.OrdinalIgnoreCase);

            var allRoots = config.FolderPaths
                .Concat(config.CharacterFolderPaths)
                .Concat(config.CoordinateFolderPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var subfolder = config.ImportSubfolder.Trim();
            var providerScopes = _importProviders
                .Where(p => p.ProviderId.Equals(authorKey.ProviderId, StringComparison.OrdinalIgnoreCase))
                .Select(GetProviderScope)
                .Append((Folder: "", UsesRatingFolders: true))
                .DistinctBy(s => s.Folder, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var gameVersionFolders = new[] { config.KoikatsuFolderName, config.KoikatsuSunshineFolderName, "" };
            var ratingFolders = new[] { config.GFolderName, config.R18FolderName, config.R18GFolderName };

            foreach (var root in allRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var providerScope in providerScopes)
                {
                    foreach (var gvFolder in gameVersionFolders)
                    {
                        foreach (var ratingFolder in providerScope.UsesRatingFolders ? ratingFolders : [""])
                        {
                            var ratingDir = BuildPath(root, subfolder, providerScope.Folder, gvFolder, ratingFolder);
                            if (!Directory.Exists(ratingDir)) continue;

                            try
                            {
                                foreach (var authorDir in Directory.EnumerateDirectories(ratingDir))
                                {
                                    ct.ThrowIfCancellationRequested();
                                    var parsed = authorProvider.TryParseFolderName(Path.GetFileName(authorDir));
                                    if (parsed is null || parsed.Key != authorKey) continue;

                                    ScanAuthorDirectory(authorDir, authorKey.ProviderId, posts, ct);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch { }
                        }
                    }
                }
            }

            var result = new List<AuthorPost>(posts.Count);
            foreach (var (id, post) in posts)
            {
                var artworkId = new ArtworkId(post.ProviderId, id);
                var provider = FindProvider(post.ProviderId);
                var distinctPaths = post.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                result.Add(new AuthorPost
                {
                    ArtworkId = artworkId,
                    ArtworkUrl = provider?.GetArtworkUrl(artworkId) ?? "",
                    Title = post.Title,
                    LocalFileCount = distinctPaths.Count,
                    LocalFilePaths = distinctPaths,
                });
            }

            result.Sort((a, b) => string.Compare(b.ArtworkId.Id, a.ArtworkId.Id, StringComparison.Ordinal));
            return result;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches detailed artwork info from the provider (or its cache).
    /// </summary>
    public Task<ArtworkInfo?> FetchArtworkDetailAsync(
        ArtworkId id,
        CancellationToken ct,
        bool saveToLocalCache)
    {
        var provider = FindProvider(id.ProviderId);
        return provider?.FetchArtworkInfoAsync(id, ct, saveToLocalCache)
            ?? Task.FromResult<ArtworkInfo?>(null);
    }

    private void ScanAuthorDirectory(
        string authorDir,
        string providerId,
        Dictionary<string, PostAccumulator> posts,
        CancellationToken ct)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(authorDir, "*.png"))
            {
                ct.ThrowIfCancellationRequested();
                var artworkId = TryParseFilename(Path.GetFileName(file), providerId);
                if (artworkId is not null)
                    AddOrUpdate(posts, artworkId.ProviderId, artworkId.Id, null, file);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(authorDir))
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(subDir);
                var artworkId = TryParseArtworkFolderName(folderName, providerId);

                var localFiles = new List<string>();
                string? titleFromFolder = artworkId is not null
                    ? ExtractTitleFromFolderName(folderName, artworkId.Id)
                    : null;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(subDir, "*.png"))
                    {
                        ct.ThrowIfCancellationRequested();
                        localFiles.Add(file);
                        if (artworkId is null)
                        {
                            var fromFile = TryParseFilename(Path.GetFileName(file), providerId);
                            if (fromFile is not null)
                                AddOrUpdate(posts, fromFile.ProviderId, fromFile.Id, null, file);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }

                if (artworkId is not null)
                    AddOrUpdate(posts, artworkId.ProviderId, artworkId.Id, titleFromFolder, localFiles);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string providerId, string id, string? title, string filePath)
        => AddOrUpdate(posts, providerId, id, title, [filePath]);

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string providerId, string id, string? title, IReadOnlyList<string> filePaths)
    {
        if (!posts.TryGetValue(id, out var post))
        {
            post = new PostAccumulator { ProviderId = providerId };
            posts[id] = post;
        }

        post.Title ??= title;
        foreach (var filePath in filePaths)
            post.FilePaths.Add(filePath);
    }

    private static string? ExtractTitleFromFolderName(string folderName, string artworkId)
    {
        var idPattern = $"({artworkId})";
        var idx = folderName.IndexOf(idPattern, StringComparison.Ordinal);
        if (idx <= 0) return null;
        var title = folderName[..idx].Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static string BuildPath(string root, string subfolder, string providerFolder, string gameVersion, string rating)
    {
        var parts = new List<string>(5) { root };
        if (!string.IsNullOrEmpty(subfolder)) parts.Add(subfolder);
        if (!string.IsNullOrEmpty(providerFolder)) parts.Add(providerFolder);
        if (!string.IsNullOrEmpty(gameVersion)) parts.Add(gameVersion);
        parts.Add(rating);
        return Path.Combine([.. parts]);
    }

    private static (string Folder, bool UsesRatingFolders) GetProviderScope(ICardImportProvider provider)
    {
        if (provider is IImportDestinationProvider destinationProvider)
            return (PathSanitizer.SanitizeRelativePath(destinationProvider.DestinationFolderName), destinationProvider.UsesRatingFolders);

        return (PathSanitizer.SanitizeRelativePath(provider.Name), true);
    }

}

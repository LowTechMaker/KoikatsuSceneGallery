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
        public string? Title { get; set; }
        public List<string> FilePaths { get; } = [];
    }

    private readonly ICardImportProvider _importProvider;
    private readonly IFolderAuthorProvider _authorProvider;
    private readonly SettingsService _settingsService;

    public AuthorPostService(
        ICardImportProvider importProvider,
        IFolderAuthorProvider authorProvider,
        SettingsService settingsService)
    {
        _importProvider = importProvider;
        _authorProvider = authorProvider;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Scans all library roots for folders belonging to <paramref name="authorId"/>
    /// and returns deduplicated artwork IDs found in subfolder names and filenames.
    /// Each result includes the folder-derived title (if any) and local file count.
    /// </summary>
    public async Task<List<AuthorPost>> ScanAuthorPostsAsync(
        AuthorKey authorKey, CancellationToken ct)
    {
        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var posts = new Dictionary<string, PostAccumulator>(StringComparer.OrdinalIgnoreCase);

            var allRoots = config.FolderPaths
                .Concat(config.CharacterFolderPaths)
                .Concat(config.CoordinateFolderPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var subfolder = config.ImportSubfolder.Trim();
            var gameVersionFolders = new[] { config.KoikatsuFolderName, config.KoikatsuSunshineFolderName, "" };
            var ratingFolders = new[] { config.GFolderName, config.R18FolderName, config.R18GFolderName };

            foreach (var root in allRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var gvFolder in gameVersionFolders)
                {
                    foreach (var ratingFolder in ratingFolders)
                    {
                        var ratingDir = BuildPath(root, subfolder, gvFolder, ratingFolder);
                        if (!Directory.Exists(ratingDir)) continue;

                        try
                        {
                            foreach (var authorDir in Directory.EnumerateDirectories(ratingDir))
                            {
                                ct.ThrowIfCancellationRequested();
                                var parsed = _authorProvider.TryParseFolderName(Path.GetFileName(authorDir));
                                if (parsed is null || parsed.Key != authorKey) continue;

                                ScanAuthorDirectory(authorDir, posts, ct);
                            }
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                }
            }

            var result = new List<AuthorPost>(posts.Count);
            foreach (var (id, post) in posts)
            {
                var artworkId = new ArtworkId(_importProvider.ProviderId, id);
                result.Add(new AuthorPost
                {
                    ArtworkId = artworkId,
                    ArtworkUrl = _importProvider.GetArtworkUrl(artworkId),
                    Title = post.Title,
                    LocalFileCount = post.FilePaths.Count,
                    LocalFilePaths = post.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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
        => _importProvider.FetchArtworkInfoAsync(id, ct, saveToLocalCache);

    private void ScanAuthorDirectory(
        string authorDir,
        Dictionary<string, PostAccumulator> posts,
        CancellationToken ct)
    {
        // Scan filenames in the author directory itself
        try
        {
            foreach (var file in Directory.EnumerateFiles(authorDir, "*.png"))
            {
                ct.ThrowIfCancellationRequested();
                var artworkId = _importProvider.TryParseFilename(Path.GetFileName(file));
                if (artworkId is not null)
                    AddOrUpdate(posts, artworkId.Id, null, file);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }

        // Scan subdirectories (artwork folders)
        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(authorDir))
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(subDir);
                var artworkId = _importProvider.TryParseArtworkFolderName(folderName);

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
                            var fromFile = _importProvider.TryParseFilename(Path.GetFileName(file));
                            if (fromFile is not null)
                                AddOrUpdate(posts, fromFile.Id, null, file);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { }

                if (artworkId is not null)
                    AddOrUpdate(posts, artworkId.Id, titleFromFolder, localFiles);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { }
    }

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string id, string? title, string filePath)
        => AddOrUpdate(posts, id, title, [filePath]);

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string id, string? title, IReadOnlyList<string> filePaths)
    {
        if (!posts.TryGetValue(id, out var post))
        {
            post = new PostAccumulator();
            posts[id] = post;
        }

        post.Title ??= title;
        foreach (var filePath in filePaths)
            post.FilePaths.Add(filePath);
    }

    private static string? ExtractTitleFromFolderName(string folderName, string artworkId)
    {
        // Artwork folders are typically formatted as "Title (123456789)" or "(123456789)"
        var idPattern = $"({artworkId})";
        var idx = folderName.IndexOf(idPattern, StringComparison.Ordinal);
        if (idx <= 0) return null;
        var title = folderName[..idx].Trim();
        return string.IsNullOrEmpty(title) ? null : title;
    }

    private static string BuildPath(string root, string subfolder, string gameVersion, string rating)
    {
        var parts = new List<string>(4) { root };
        if (!string.IsNullOrEmpty(subfolder)) parts.Add(subfolder);
        if (!string.IsNullOrEmpty(gameVersion)) parts.Add(gameVersion);
        parts.Add(rating);
        return Path.Combine([.. parts]);
    }
}

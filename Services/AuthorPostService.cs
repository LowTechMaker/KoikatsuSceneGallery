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
        public required string ArtworkId { get; init; }
        public string? Title { get; set; }
        public PostMetadataDocument? Metadata { get; set; }
        public List<string> FilePaths { get; } = [];
        public HashSet<string> AuthorDirectories { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly IReadOnlyList<ICardImportProvider> _importProviders;
    private readonly IReadOnlyList<IFolderAuthorProvider> _authorProviders;
    private readonly SettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly PostMetadataStore _postMetadataStore = new();

    public AuthorPostService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        SettingsService settingsService,
        IAppLogger logger)
    {
        _importProviders = importProviders;
        _authorProviders = authorProviders;
        _settingsService = settingsService;
        _logger = logger;
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
        => (await ScanAuthorPostDataAsync(authorKey, ct).ConfigureAwait(false))
            .Posts
            .ToList();

    /// <summary>
    /// Scans the author's local folders, returning both recognized posts and
    /// local PNG files for which no safe post association exists.
    /// </summary>
    public async Task<AuthorPostScanResult> ScanAuthorPostDataAsync(
        AuthorKey authorKey, CancellationToken ct)
    {
        var authorProvider = FindAuthorProvider(authorKey.ProviderId);
        if (authorProvider is null) return new([], []);

        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var posts = new Dictionary<string, PostAccumulator>(StringComparer.OrdinalIgnoreCase);
            var scannedImages = new List<UnassignedAuthorImage>();

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

                                    ScanAuthorDirectory(authorDir, authorKey, posts, scannedImages, ct);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { _logger.LogError("AuthorPosts.ScanRatingDirectory", ex, ratingDir); }
                        }
                    }
                }
            }

            var result = new List<AuthorPost>(posts.Count);
            foreach (var post in posts.Values)
            {
                var artworkId = new ArtworkId(post.ProviderId, post.ArtworkId);
                var provider = FindProvider(post.ProviderId);
                var distinctPaths = post.FilePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var metadata = post.Metadata;
                result.Add(new AuthorPost
                {
                    ArtworkId = artworkId,
                    ArtworkUrl = provider?.GetArtworkUrl(artworkId) ?? "",
                    Title = metadata?.Title ?? post.Title,
                    Description = metadata?.Description,
                    Rating = metadata is null
                        ? ContentRating.AllAges
                        : (ContentRating)metadata.Rating,
                    Tags = metadata?.Tags
                        .Select(static tag => new ArtworkTag(tag.Name, tag.TranslatedName))
                        .ToList(),
                    IsDetailLoaded = metadata is not null,
                    IsSaved = metadata is not null,
                    LocalFileCount = distinctPaths.Count,
                    LocalFilePaths = distinctPaths,
                    AuthorDirectories = [.. post.AuthorDirectories],
                });
            }

            result.Sort((a, b) => string.Compare(b.ArtworkId.Id, a.ArtworkId.Id, StringComparison.Ordinal));
            var assignedPaths = new HashSet<string>(
                posts.Values.SelectMany(static post => post.FilePaths),
                StringComparer.OrdinalIgnoreCase);
            var unassigned = scannedImages
                .Where(image => !assignedPaths.Contains(image.FilePath))
                .GroupBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(image => image.FileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new AuthorPostScanResult(result, unassigned);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Gives selected unclassified local images an explicit artwork identity.
    /// The relationship is persisted in an artwork-ID folder, while an
    /// existing sidecar (when present) is updated with the added filenames.
    /// </summary>
    public async Task AssignUnclassifiedImagesAsync(
        AuthorKey authorKey,
        IReadOnlyList<UnassignedAuthorImage> images,
        string artworkId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(images);
        var provider = FindProvider(authorKey.ProviderId)
            ?? throw new InvalidOperationException("The artwork provider is unavailable.");
        var submittedArtworkReference = artworkId.Trim();
        var parsedArtwork = provider.TryParseUrl(submittedArtworkReference);
        var normalizedArtworkId = parsedArtwork?.Id ?? submittedArtworkReference;
        if (parsedArtwork is not null
            && !parsedArtwork.ProviderId.Equals(
                authorKey.ProviderId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The artwork URL belongs to a different provider.", nameof(artworkId));
        }
        if (string.IsNullOrWhiteSpace(normalizedArtworkId)
            || normalizedArtworkId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || normalizedArtworkId.Contains(Path.DirectorySeparatorChar)
            || normalizedArtworkId.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException("Enter a valid artwork ID or artwork URL.", nameof(artworkId));
        }

        var selectedImages = images
            .GroupBy(image => image.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
        if (selectedImages.Count == 0)
            return;

        foreach (var image in selectedImages)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(image.FilePath))
                throw new FileNotFoundException("A selected local file no longer exists.", image.FilePath);
            if (!IsWithinDirectory(image.FilePath, image.AuthorDirectory))
                throw new InvalidOperationException("A selected file is outside its author directory.");
        }

        var artwork = new ArtworkId(authorKey.ProviderId, normalizedArtworkId);
        var destinations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var authorDirectory in selectedImages
                     .Select(static image => image.AuthorDirectory)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            destinations[authorDirectory] = FindArtworkDirectory(
                authorDirectory,
                provider,
                artwork)
                ?? Path.Combine(authorDirectory, $"[SCENE] ({normalizedArtworkId})");
        }

        var assignments = selectedImages
            .Select(image => new ArtworkFileAssignment(
                image.FilePath,
                destinations[image.AuthorDirectory]))
            .ToList();
        await Task.Run(
            () => new ArtworkFileAssignmentService().Move(assignments, ct),
            ct).ConfigureAwait(false);

        foreach (var authorDirectory in destinations.Keys)
        {
            ct.ThrowIfCancellationRequested();
            var document = _postMetadataStore.Read(
                authorDirectory,
                authorKey.ProviderId,
                normalizedArtworkId);
            if (document is null)
                continue;

            var fileNames = selectedImages
                .Where(image => image.AuthorDirectory.Equals(
                    authorDirectory,
                    StringComparison.OrdinalIgnoreCase))
                .Select(static image => image.FileName)
                .ToList();
            await _postMetadataStore.WriteAsync(
                authorDirectory,
                document with
                {
                    SchemaVersion = PostMetadataDocument.CurrentSchemaVersion,
                    LocalFileNames = fileNames,
                },
                ct).ConfigureAwait(false);
        }
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

    /// <summary>
    /// Fetches an artwork's details and persists them next to every local copy
    /// of the author's library.  The sidecar remains usable after app-cache
    /// eviction or a provider plugin becoming temporarily unavailable.
    /// </summary>
    public async Task<ArtworkInfo?> FetchArtworkDetailAsync(
        AuthorPost post,
        CancellationToken ct,
        bool saveToLocalCache)
    {
        ArgumentNullException.ThrowIfNull(post);

        var info = await FetchArtworkDetailAsync(
            post.ArtworkId,
            ct,
            saveToLocalCache).ConfigureAwait(false);
        if (info is null)
            return null;

        foreach (var authorDirectory in post.AuthorDirectories
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!post.LocalFilePaths.Any(path =>
                    File.Exists(path) && IsWithinDirectory(path, authorDirectory)))
            {
                continue;
            }

            try
            {
                var localFileNames = post.LocalFilePaths
                    .Where(path => File.Exists(path) && IsWithinDirectory(path, authorDirectory))
                    .Select(Path.GetFileName)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Select(static name => name!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                await _postMetadataStore.WriteAsync(
                    authorDirectory,
                    PostMetadataMapper.ToDocument(info, localFileNames),
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "AuthorPosts.WritePostMetadata",
                    ex,
                    _postMetadataStore.GetSidecarPath(
                        authorDirectory,
                        info.ArtworkId.ProviderId,
                        info.ArtworkId.Id));
            }
        }

        return info;
    }

    private void ScanAuthorDirectory(
        string authorDir,
        AuthorKey authorKey,
        Dictionary<string, PostAccumulator> posts,
        List<UnassignedAuthorImage> scannedImages,
        CancellationToken ct)
    {
        var providerId = authorKey.ProviderId;
        var scanSucceeded = true;
        var scannedFileNames = new ScannedFileNameIndex();
        try
        {
            foreach (var file in Directory.EnumerateFiles(authorDir, "*.png"))
            {
                ct.ThrowIfCancellationRequested();
                scannedImages.Add(new(file, authorDir));
                var artworkId = TryParseFilename(Path.GetFileName(file), providerId);
                scannedFileNames.Record(file, artworkId?.Id);
                if (artworkId is not null)
                    AddOrUpdate(posts, artworkId.ProviderId, artworkId.Id, null, file, authorDir);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            scanSucceeded = false;
            _logger.LogError("AuthorPosts.ScanAuthorFiles", ex, authorDir);
        }

        try
        {
            foreach (var subDir in Directory.EnumerateDirectories(authorDir))
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(subDir);
                if (folderName.Equals(
                        PostMetadataStore.MetadataDirectoryName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
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
                        scannedImages.Add(new(file, authorDir));
                        localFiles.Add(file);
                        if (artworkId is not null)
                        {
                            scannedFileNames.Record(file, artworkId.Id);
                            continue;
                        }

                        var fromFile = TryParseFilename(Path.GetFileName(file), providerId);
                        scannedFileNames.Record(file, fromFile?.Id);
                        if (fromFile is not null)
                            AddOrUpdate(
                                posts,
                                fromFile.ProviderId,
                                fromFile.Id,
                                null,
                                file,
                                authorDir);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    scanSucceeded = false;
                    _logger.LogError("AuthorPosts.ScanArtworkFiles", ex, subDir);
                }

                if (artworkId is not null && localFiles.Count > 0)
                    AddOrUpdate(
                        posts,
                        artworkId.ProviderId,
                        artworkId.Id,
                        titleFromFolder,
                        localFiles,
                        authorDir);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            scanSucceeded = false;
            _logger.LogError("AuthorPosts.ScanAuthorDirectories", ex, authorDir);
        }

        ReconcileSidecars(authorDir, authorKey, posts, scannedFileNames, scanSucceeded, ct);
    }

    private void ReconcileSidecars(
        string authorDir,
        AuthorKey authorKey,
        Dictionary<string, PostAccumulator> posts,
        ScannedFileNameIndex scannedFileNames,
        bool deleteOrphans,
        CancellationToken ct)
    {
        IReadOnlyList<PostMetadataDocument> documents;
        try
        {
            documents = _postMetadataStore.ReadAll(authorDir);
        }
        catch (Exception ex)
        {
            _logger.LogError("AuthorPosts.ReadPostMetadata", ex, authorDir);
            return;
        }

        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            if (!document.ProviderId.Equals(authorKey.ProviderId, StringComparison.OrdinalIgnoreCase)
                || !document.AuthorId.Equals(authorKey.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = BuildPostKey(document.ProviderId, document.ArtworkId);
            PostAccumulator? existingPost = null;
            if (posts.TryGetValue(key, out var post)
                && post.AuthorDirectories.Contains(authorDir))
            {
                existingPost = post;
                if (existingPost.Metadata is null || document.FetchedAt > existingPost.Metadata.FetchedAt)
                    existingPost.Metadata = document;
            }

            // Version 2 sidecars remember the local filenames. This recovers
            // manually assigned root files whose filenames/folder names carry
            // no artwork ID. This must still run when the post was found from
            // an artwork folder: a post can legitimately contain both folder
            // based files and legacy root files tracked by the sidecar.
            var match = scannedFileNames.Match(document.ArtworkId, document.LocalFileNames);
            if (match.Files.Count > 0)
            {
                AddOrUpdate(
                    posts,
                    document.ProviderId,
                    document.ArtworkId,
                    null,
                    match.Files,
                    authorDir);
                if (posts.TryGetValue(key, out var matchedPost)
                    && (matchedPost.Metadata is null
                        || document.FetchedAt > matchedPost.Metadata.FetchedAt))
                {
                    matchedPost.Metadata = document;
                }
                continue;
            }

            // A folder-derived post is valid even if its sidecar has no file
            // names (for example an old v1 sidecar), so never treat it as an
            // orphan merely because the filename match found nothing.
            if (existingPost is not null)
                continue;

            if (!deleteOrphans || match.HasAmbiguousName)
                continue;

            try
            {
                _postMetadataStore.Delete(authorDir, document.ProviderId, document.ArtworkId);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "AuthorPosts.DeleteOrphanPostMetadata",
                    ex,
                    _postMetadataStore.GetSidecarPath(
                        authorDir,
                        document.ProviderId,
                        document.ArtworkId));
            }
        }
    }

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string providerId,
        string id,
        string? title,
        string filePath,
        string authorDirectory)
        => AddOrUpdate(posts, providerId, id, title, [filePath], authorDirectory);

    private static void AddOrUpdate(
        Dictionary<string, PostAccumulator> posts,
        string providerId,
        string id,
        string? title,
        IReadOnlyList<string> filePaths,
        string authorDirectory)
    {
        var key = BuildPostKey(providerId, id);
        if (!posts.TryGetValue(key, out var post))
        {
            post = new PostAccumulator
            {
                ProviderId = providerId,
                ArtworkId = id,
            };
            posts[key] = post;
        }

        post.Title ??= title;
        foreach (var filePath in filePaths)
            post.FilePaths.Add(filePath);
        if (filePaths.Count > 0)
            post.AuthorDirectories.Add(authorDirectory);
    }

    private static string BuildPostKey(string providerId, string artworkId)
        => $"{providerId}\u001F{artworkId}";

    private static string? FindArtworkDirectory(
        string authorDirectory,
        ICardImportProvider provider,
        ArtworkId artwork)
    {
        foreach (var directory in Directory.EnumerateDirectories(authorDirectory))
        {
            var parsed = provider.TryParseArtworkFolderName(Path.GetFileName(directory));
            if (parsed is not null
                && parsed.ProviderId.Equals(artwork.ProviderId, StringComparison.OrdinalIgnoreCase)
                && parsed.Id.Equals(artwork.Id, StringComparison.OrdinalIgnoreCase))
            {
                return directory;
            }
        }

        return null;
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var relativePath = Path.GetRelativePath(directory, path);
        return !relativePath.Equals("..", StringComparison.Ordinal)
            && !relativePath.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal);
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

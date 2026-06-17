using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Orchestrates the import pipeline: card type classification, provider API
/// fetch (author + tags + rating), destination folder resolution, and file move.
/// </summary>
public sealed class ImportService
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private static string GetRatingFolder(ContentRating rating, SettingsService.ConfigData config) => rating switch
    {
        ContentRating.R18 => config.R18FolderName,
        ContentRating.R18G => config.R18GFolderName,
        _ => config.GFolderName,
    };

    private static string GetGameVersionFolder(GameVersion gameVersion, SettingsService.ConfigData config) => gameVersion switch
    {
        GameVersion.Koikatsu         => config.KoikatsuFolderName,
        GameVersion.KoikatsuSunshine => config.KoikatsuSunshineFolderName,
        _ => "",
    };

    private (string Folder, bool UsesRatingFolders) GetProviderScope(string providerId)
    {
        var provider = FindProvider(providerId);
        if (provider is IImportDestinationProvider dest)
            return (SanitizeRelativePath(dest.DestinationFolderName), dest.UsesRatingFolders);
        var name = provider?.Name ?? providerId;
        return (SanitizeRelativePath(name), true);
    }

    private static string[] GetRatingFolderNames(SettingsService.ConfigData config) =>
        [config.GFolderName, config.R18FolderName, config.R18GFolderName];

    private static string[] GetGameVersionFolderNames(SettingsService.ConfigData config) =>
        [config.KoikatsuFolderName, config.KoikatsuSunshineFolderName, ""];

    private static string FormatAuthorFolder(SettingsService.ConfigData config, string authorName, string authorId) =>
        SanitizeFolderName(config.AuthorFolderFormat
            .Replace("{name}", authorName)
            .Replace("{id}", authorId));

    private static string FormatArtworkFolder(SettingsService.ConfigData config, string? title, string artworkId) =>
        string.IsNullOrEmpty(title)
            ? $"({artworkId})"
            : SanitizeFolderName(config.ArtworkFolderFormat
                .Replace("{title}", title)
                .Replace("{id}", artworkId));

    private readonly IReadOnlyList<ICardImportProvider> _importProviders;
    private readonly IReadOnlyList<IFolderAuthorProvider> _authorProviders;
    private readonly IReverseImageSearchProvider? _reverseImageSearchProvider;
    private readonly SettingsService _settingsService;

    public ImportService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        IReverseImageSearchProvider? reverseImageSearchProvider,
        SettingsService settingsService)
    {
        _importProviders = importProviders;
        _authorProviders = authorProviders;
        _reverseImageSearchProvider = reverseImageSearchProvider;
        _settingsService = settingsService;
    }

    private ArtworkId? TryParseFilenameAll(string fileName)
    {
        foreach (var provider in _importProviders)
        {
            var id = provider.TryParseFilename(fileName);
            if (id is not null) return id;
        }
        return null;
    }

    private ICardImportProvider? FindProvider(string providerId)
        => _importProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));

    private IFolderAuthorProvider? FindAuthorProvider(string providerId)
        => _authorProviders.FirstOrDefault(p => p.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
           ?? FindProvider(providerId) as IFolderAuthorProvider;

    public async Task ComputeFingerprintsAsync(IReadOnlyList<ImportItem> items, CancellationToken ct)
    {
        await Parallel.ForEachAsync(items, new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount,
            CancellationToken = ct,
        }, async (item, token) =>
        {
            var fp = await ImageFingerprintService.ComputeAsync(item.SourceFilePath, token).ConfigureAwait(false);
            if (fp is not null)
            {
                item.PHash = fp.Value.PHash;
                item.ColorHistogram = fp.Value.Histogram;
            }
        }).ConfigureAwait(false);
    }

    public ArtworkId CreateManualArtworkId(string id)
    {
        var trimmed = id.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
        {
            foreach (var provider in _importProviders)
            {
                var parsed = provider.TryParseUrl(trimmed);
                if (parsed is not null) return parsed;
            }
        }

        foreach (var provider in _importProviders)
        {
            var parsed = provider.TryParseFilename(trimmed);
            if (parsed is not null) return parsed;
        }
        return new(_importProviders[0].ProviderId, trimmed);
    }

    public async Task<ArtworkInfo?> FetchArtworkInfoAsync(ArtworkId artworkId, CancellationToken ct)
    {
        var provider = FindProvider(artworkId.ProviderId);
        if (provider is null) return null;

        try
        {
            return await provider.FetchArtworkInfoAsync(artworkId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<AuthorInfo?> FetchAuthorInfoAsync(
        AuthorKey authorKey,
        bool forceRefresh,
        CancellationToken ct)
    {
        var provider = FindAuthorProvider(authorKey.ProviderId);
        if (provider is null) return null;

        try
        {
            return await provider.GetAuthorInfoAsync(authorKey, forceRefresh, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public async Task<ReverseImageSearchResult?> SearchReverseImageAsync(
        string imagePath,
        string apiKey,
        CancellationToken ct)
    {
        if (_reverseImageSearchProvider is null)
            return null;

        try
        {
            return await _reverseImageSearchProvider.SearchImageAsync(
                imagePath, apiKey, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Analyzes a batch of dropped file paths: classifies card type on a
    /// background thread (keeping the UI responsive), adds valid cards to the
    /// collection as they are found, then fetches artwork metadata and resolves
    /// destination folders. Items in the collection are updated on the dispatcher
    /// thread as results arrive.
    /// </summary>
    public async Task<int> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct)
    {
        // Phase 1: classify on a background thread so the UI stays responsive
        // (CardTypeClassifier reads file content for each card).
        var (validItems, rejectedCount) = await Task.Run(() =>
        {
            var valid = new List<ImportItem>(filePaths.Count);
            int rejected = 0;
            foreach (var path in filePaths)
            {
                ct.ThrowIfCancellationRequested();
                var (cardType, gameVersion) = CardTypeClassifier.ClassifyExtended(path);
                if (cardType == CardType.NotACard) { rejected++; continue; }
                var item = new ImportItem { SourceFilePath = path, Status = ImportItemStatus.Analyzing };
                item.CardType = cardType;
                item.GameVersion = gameVersion;
                item.ArtworkId = TryParseFilenameAll(item.FileName);
                valid.Add(item);
            }
            return (valid, rejected);
        }, ct);

        // Back on the UI thread: add valid items to the collection.
        // The first Add flips HasItems → the DropZone transitions to the queue view.
        foreach (var item in validItems)
            items.Add(item);

        // Phase 2: deduplicate artwork IDs and fetch metadata from provider
        var groups = validItems
            .Where(i => i.ArtworkId is not null)
            .GroupBy(i => i.ArtworkId!.Id)
            .ToList();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var artworkId = group.First().ArtworkId!;
            var provider = FindProvider(artworkId.ProviderId);
            ArtworkInfo? info;
            try
            {
                info = provider is not null
                    ? await provider.FetchArtworkInfoAsync(artworkId, ct).ConfigureAwait(false)
                    : null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                info = null;
            }

            dispatcher.TryEnqueue(() =>
            {
                foreach (var item in group)
                {
                    if (info is not null)
                    {
                        item.AuthorName = info.AuthorName;
                        item.AuthorId = info.AuthorId;
                        item.Title = info.Title;
                        item.Rating = info.Rating;
                        item.Tags = info.Tags;
                    }
                    item.Status = ImportItemStatus.ReadyToImport;
                }
            });
        }

        // Mark items without an artwork ID as ready
        dispatcher.TryEnqueue(() =>
        {
            foreach (var item in validItems.Where(i => i.ArtworkId is null))
                item.Status = ImportItemStatus.ReadyToImport;
        });

        // Phase 3: resolve destinations (initial pass; fingerprints not yet computed,
        // so visual similarity falls back to count threshold here — a second
        // ReResolveAsync call after ComputeFingerprintsAsync corrects this)
        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);
        var existingFilenames = await Task.Run(() => BuildExistingFilenameSet(config), ct).ConfigureAwait(false);
        dispatcher.TryEnqueue(() => ResolveDestinations(items, config, existingFilenames,
            config.ArtworkSubfolderThreshold, config.UseVisualSimilarity));

        return rejectedCount;
    }

    /// <summary>
    /// Scans all library roots for author folders, returning deduplicated authors
    /// sorted by name. Used by the manual-assign picker.
    /// </summary>
    public async Task<List<(string Name, string Id)>> GetKnownAuthorsAsync(CancellationToken ct)
    {
        if (_authorProviders.Count == 0) return [];

        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<(string Name, string Id)>();
            var subfolder = config.ImportSubfolder.Trim();

            var allRoots = config.FolderPaths
                .Concat(config.CharacterFolderPaths)
                .Concat(config.CoordinateFolderPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var root in allRoots)
            {
                if (!Directory.Exists(root)) continue;

                foreach (var authorProvider in _authorProviders)
                {
                    var providerScopes = _importProviders
                        .Where(p => p.ProviderId.Equals(authorProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
                        .Select(p => GetProviderScope(p.ProviderId))
                        .Append((Folder: "", UsesRatingFolders: true))
                        .DistinctBy(s => $"{s.Folder}\u001F{s.UsesRatingFolders}");

                    foreach (var providerScope in providerScopes)
                    {
                        foreach (var gameVersionFolder in GetGameVersionFolderNames(config))
                        {
                            foreach (var ratingFolder in providerScope.UsesRatingFolders ? GetRatingFolderNames(config) : [""])
                            {
                                var ratingDir = BuildTargetBase(root, subfolder, providerScope.Folder, gameVersionFolder, ratingFolder, null);
                                if (!Directory.Exists(ratingDir)) continue;
                                try
                                {
                                    foreach (var dir in Directory.EnumerateDirectories(ratingDir))
                                    {
                                        ct.ThrowIfCancellationRequested();
                                        var parsed = authorProvider.TryParseFolderName(Path.GetFileName(dir));
                                        if (parsed is not null && seen.Add($"{parsed.Key.ProviderId}\u001F{parsed.Key.Id}"))
                                            result.Add((parsed.FolderDisplayName, parsed.Key.Id));
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                                catch { }
                            }
                        }
                    }
                }
            }

            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-resolves destination paths for all ReadyToImport items. Called after
    /// manual author assignment so the newly-assigned items get proper paths.
    /// </summary>
    public async Task ReResolveAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct,
        int? artworkSubfolderThreshold = null,
        bool? useVisualSimilarity = null)
    {
        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);
        var existingFilenames = await Task.Run(() => BuildExistingFilenameSet(config), ct).ConfigureAwait(false);
        int threshold = artworkSubfolderThreshold ?? config.ArtworkSubfolderThreshold;
        bool visual = useVisualSimilarity ?? config.UseVisualSimilarity;
        dispatcher.TryEnqueue(() => ResolveDestinations(items, config, existingFilenames, threshold, visual));
    }

    /// <summary>
    /// Moves all ReadyToImport (non-excluded) items to their resolved destinations.
    /// </summary>
    public async Task ImportAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct)
    {
        var toImport = items
            .Where(i => i.Status == ImportItemStatus.ReadyToImport && i.DestinationPath is not null)
            .ToList();

        foreach (var item in toImport)
        {
            ct.ThrowIfCancellationRequested();
            dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Importing);

            try
            {
                await Task.Run(() =>
                {
                    var destDir = Path.GetDirectoryName(item.DestinationPath!)!;
                    Directory.CreateDirectory(destDir);

                    if (File.Exists(item.DestinationPath))
                    {
                        if (FilesAreIdentical(item.SourceFilePath, item.DestinationPath!))
                        {
                            File.Delete(item.SourceFilePath);
                            dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Completed);
                        }
                        else
                        {
                            dispatcher.TryEnqueue(() =>
                            {
                                item.Status = ImportItemStatus.Skipped;
                                item.ErrorMessage = "File already exists";
                            });
                        }
                        return;
                    }

                    File.Move(item.SourceFilePath, item.DestinationPath!);
                    dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Completed);
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                dispatcher.TryEnqueue(() =>
                {
                    item.Status = ImportItemStatus.Failed;
                    item.ErrorMessage = ex.Message;
                });
            }
        }

        // Clean up empty source directories after all moves complete.
        await Task.Run(() =>
        {
            var sourceDirs = toImport
                .Where(i => i.Status == ImportItemStatus.Completed)
                .Select(i => Path.GetDirectoryName(i.SourceFilePath)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(d => d.Length);

            foreach (var dir in sourceDirs)
            {
                try
                {
                    if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { }
            }
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private void ResolveDestinations(
        ObservableCollection<ImportItem> items,
        SettingsService.ConfigData config,
        HashSet<string> existingFilenames,
        int artworkThreshold = 1,
        bool useVisualSimilarity = false)
    {
        var folderIndex = BuildFolderIndex(config);
        var subfolder = config.ImportSubfolder.Trim();

        var readyWithArtwork = items
            .Where(i => i.Status == ImportItemStatus.ReadyToImport && i.ArtworkId is not null)
            .GroupBy(i => i.ArtworkId!.Id)
            .ToList();

        var artworkCounts = readyWithArtwork.ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var artworkGroups = readyWithArtwork.ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<ImportItem>)g.ToList(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item.Status != ImportItemStatus.ReadyToImport || item.CardType == CardType.NotACard)
                continue;

            if (existingFilenames.Contains(item.FileName))
            {
                item.Status = ImportItemStatus.AlreadyInLibrary;
                continue;
            }

            var roots = GetRootsForCardType(item.CardType, config);
            if (roots.Count == 0) continue;

            var scope = item.ArtworkId is not null ? GetProviderScope(item.ArtworkId.ProviderId) : (Folder: "", UsesRatingFolders: true);
            var ratingFolder = scope.UsesRatingFolders ? GetRatingFolder(item.Rating, config) : "";
            var gameVersionFolder = GetGameVersionFolder(item.GameVersion, config);
            string? targetFolder = null;

            if (item.AuthorId is not null)
            {
                foreach (var root in roots)
                {
                    var scopeKey = BuildScopeKey(root, scope.Folder, gameVersionFolder, ratingFolder);
                    if (folderIndex.TryGetValue(scopeKey, out var authorFolders)
                        && authorFolders.TryGetValue(item.AuthorId, out var existing))
                    {
                        targetFolder = existing;
                        break;
                    }
                }

                if (targetFolder is null && item.AuthorName is not null)
                {
                    var safeName = FormatAuthorFolder(config, item.AuthorName, item.AuthorId);
                    targetFolder = BuildTargetBase(roots[0], subfolder, scope.Folder, gameVersionFolder, ratingFolder, safeName);
                }
            }

            targetFolder ??= BuildTargetBase(roots[0], subfolder, scope.Folder, gameVersionFolder, config.UnknownFolderName, null);

            if (item.ArtworkId is not null)
            {
                var artworkSafeName = FormatArtworkFolder(config, item.Title, item.ArtworkId.Id);
                var artworkFolder = Path.Combine(targetFolder, artworkSafeName);

                if (Directory.Exists(artworkFolder))
                {
                    targetFolder = artworkFolder;
                }
                else
                {
                    bool countExceeds = artworkThreshold >= 0
                        && artworkCounts.TryGetValue(item.ArtworkId.Id, out var cnt)
                        && cnt > artworkThreshold;

                    if (useVisualSimilarity)
                    {
                        var artworkItems = artworkGroups.TryGetValue(item.ArtworkId.Id, out var ag) ? ag : null;
                        var visualVerdict = artworkItems is not null
                            ? CardGroupingService.ShouldGroupAsArtwork(artworkItems) : null;

                        bool shouldCreate = visualVerdict switch
                        {
                            true => true,
                            false => false,
                            null => countExceeds,
                        };
                        if (shouldCreate)
                            targetFolder = artworkFolder;
                    }
                    else if (countExceeds)
                    {
                        targetFolder = artworkFolder;
                    }
                }
            }

            item.DestinationPath = Path.Combine(targetFolder, item.FileName);
        }
    }

    // Builds the path segments: root[\subfolder][\provider][\gameVersion][\rating][\authorName]
    private static string BuildTargetBase(
        string root,
        string subfolder,
        string providerFolder,
        string gameVersionFolder,
        string ratingFolder,
        string? authorName)
    {
        var parts = new List<string>(6) { root };
        if (!string.IsNullOrEmpty(subfolder)) parts.Add(subfolder);
        if (!string.IsNullOrEmpty(providerFolder)) parts.Add(providerFolder);
        if (!string.IsNullOrEmpty(gameVersionFolder)) parts.Add(gameVersionFolder);
        if (!string.IsNullOrEmpty(ratingFolder)) parts.Add(ratingFolder);
        if (authorName is not null) parts.Add(authorName);
        return Path.Combine([.. parts]);
    }

    private HashSet<string> BuildExistingFilenameSet(SettingsService.ConfigData config)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allRoots = config.FolderPaths
            .Concat(config.CharacterFolderPaths)
            .Concat(config.CoordinateFolderPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in allRoots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                    set.Add(Path.GetFileName(file));
            }
            catch { }
        }

        return set;
    }

    private Dictionary<string, Dictionary<string, string>> BuildFolderIndex(SettingsService.ConfigData config)
    {
        // (root, providerFolder, gameVersionFolder, ratingFolder) → authorId → fullFolderPath
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var subfolder = config.ImportSubfolder.Trim();

        var allRoots = config.FolderPaths
            .Concat(config.CharacterFolderPaths)
            .Concat(config.CoordinateFolderPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        var providerScopes = _importProviders
            .Select(p =>
            {
                var s = GetProviderScope(p.ProviderId);
                return (
                    p.ProviderId,
                    s.Folder,
                    RatingFolders: s.UsesRatingFolders
                        ? GetRatingFolderNames(config)
                        : [""]
                );
            })
            .ToList();

        foreach (var root in allRoots)
        {
            if (!Directory.Exists(root)) continue;

            foreach (var providerScope in providerScopes)
            {
                foreach (var gameVersionFolder in GetGameVersionFolderNames(config))
                {
                    foreach (var ratingFolder in providerScope.RatingFolders)
                    {
                        var ratingDir = BuildTargetBase(root, subfolder, providerScope.Folder, gameVersionFolder, ratingFolder, null);
                        var authorFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (Directory.Exists(ratingDir))
                        {
                            try
                            {
                                foreach (var dir in Directory.EnumerateDirectories(ratingDir))
                                {
                                    var parsed = FindAuthorProvider(providerScope.ProviderId)?.TryParseFolderName(Path.GetFileName(dir));
                                    if (parsed is not null)
                                        authorFolders.TryAdd(parsed.Key.Id, dir);
                                }
                            }
                            catch { }
                        }

                        index[BuildScopeKey(root, providerScope.Folder, gameVersionFolder, ratingFolder)] = authorFolders;
                    }
                }
            }
        }

        return index;
    }

    private static List<string> GetRootsForCardType(CardType cardType, SettingsService.ConfigData config)
    {
        return cardType switch
        {
            CardType.Scene => config.FolderPaths,
            CardType.Character => config.CharacterFolderPaths,
            CardType.Coordinate => config.CoordinateFolderPaths,
            _ => [],
        };
    }

    private static string BuildScopeKey(
        string root,
        string providerFolder,
        string gameVersionFolder,
        string ratingFolder)
        => string.Join('\u001F', root, providerFolder, gameVersionFolder, ratingFolder);

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in InvalidFileNameChars)
            name = name.Replace(c, '_');
        return name.Trim();
    }

    private static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeFolderName)
            .Where(p => p.Length > 0)
            .ToArray();

        return parts.Length == 0 ? "" : Path.Combine(parts);
    }

    private static bool FilesAreIdentical(string pathA, string pathB)
    {
        var infoA = new FileInfo(pathA);
        var infoB = new FileInfo(pathB);
        if (infoA.Length != infoB.Length) return false;

        const int bufferSize = 1 << 16;
        var bufA = new byte[bufferSize];
        var bufB = new byte[bufferSize];

        using var fsA = infoA.OpenRead();
        using var fsB = infoB.OpenRead();

        while (true)
        {
            int readA = fsA.ReadAtLeast(bufA, bufferSize, throwOnEndOfStream: false);
            int readB = fsB.ReadAtLeast(bufB, bufferSize, throwOnEndOfStream: false);
            if (readA != readB) return false;
            if (readA == 0) return true;
            if (!bufA.AsSpan(0, readA).SequenceEqual(bufB.AsSpan(0, readB))) return false;
        }
    }
}

using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Helpers;
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
    private const string UnrecognizedFolderName = "!unrecognized";

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
            return (PathSanitizer.SanitizeRelativePath(dest.DestinationFolderName), dest.UsesRatingFolders);
        var name = provider?.Name ?? providerId;
        return (PathSanitizer.SanitizeRelativePath(name), true);
    }

    private static string[] GetRatingFolderNames(SettingsService.ConfigData config) =>
        [config.GFolderName, config.R18FolderName, config.R18GFolderName];

    private static string[] GetGameVersionFolderNames(SettingsService.ConfigData config) =>
        [config.KoikatsuFolderName, config.KoikatsuSunshineFolderName, ""];

    private static ImportPathOptions GetPathOptions(SettingsService.ConfigData config)
        => new(config.ImportSubfolder, config.AuthorFolderFormat, config.ArtworkFolderFormat);

    private static string FormatAuthorFolder(SettingsService.ConfigData config, string authorName, string authorId)
        => ImportDestinationPolicy.FormatAuthorFolder(GetPathOptions(config), authorName, authorId);

    private static string FormatArtworkFolder(SettingsService.ConfigData config, string? title, string artworkId)
        => ImportDestinationPolicy.FormatArtworkFolder(GetPathOptions(config), title, artworkId);

    private readonly IReadOnlyList<ICardImportProvider> _importProviders;
    private readonly IReadOnlyList<IFolderAuthorProvider> _authorProviders;
    private readonly IReverseImageSearchProvider? _reverseImageSearchProvider;
    private readonly SettingsService _settingsService;
    private readonly IAppLogger _logger;
    private readonly ImportFileExecutor _fileExecutor;

    public ImportService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        IReverseImageSearchProvider? reverseImageSearchProvider,
        SettingsService settingsService,
        IAppLogger logger)
    {
        _importProviders = importProviders;
        _authorProviders = authorProviders;
        _reverseImageSearchProvider = reverseImageSearchProvider;
        _settingsService = settingsService;
        _logger = logger;
        _fileExecutor = new ImportFileExecutor(logger);
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
        catch (Exception ex)
        {
            _logger.LogError("Import.FetchArtwork", ex, artworkId.Id);
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
        catch (Exception ex)
        {
            _logger.LogError("Import.FetchAuthor", ex, authorKey.Id);
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
        catch (Exception ex)
        {
            _logger.LogError("Import.ReverseImageSearch", ex, imagePath);
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
            catch (Exception ex)
            {
                _logger.LogError("Import.FetchArtworkMetadata", ex, artworkId?.Id);
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
                        item.AuthorProviderId = artworkId!.ProviderId;
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
        var (existingFilenames, folderIndex) = await BuildLibraryIndexAsync(config, ct).ConfigureAwait(false);
        dispatcher.TryEnqueue(() => ResolveDestinations(items, config, existingFilenames, folderIndex,
            config.ArtworkSubfolderThreshold, config.UseVisualSimilarity));

        return rejectedCount;
    }

    /// <summary>
    /// Scans all library roots for author folders, returning deduplicated authors
    /// sorted by name. Used by the manual-assign picker.
    /// </summary>
    public async Task<List<(string Name, string Id, string ProviderId)>> GetKnownAuthorsAsync(CancellationToken ct)
    {
        if (_authorProviders.Count == 0) return [];

        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);

        return await Task.Run(() =>
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<(string Name, string Id, string ProviderId)>();
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
                                            result.Add((parsed.FolderDisplayName, parsed.Key.Id, parsed.Key.ProviderId));
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex) { _logger.LogError("Import.ScanAuthorDirectory", ex, ratingDir); }
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
        var (existingFilenames, folderIndex) = await BuildLibraryIndexAsync(config, ct).ConfigureAwait(false);
        int threshold = artworkSubfolderThreshold ?? config.ArtworkSubfolderThreshold;
        bool visual = useVisualSimilarity ?? config.UseVisualSimilarity;
        dispatcher.TryEnqueue(() => ResolveDestinations(
            items, config, existingFilenames, folderIndex, threshold, visual));
    }

    /// <summary>
    /// Moves all ReadyToImport (non-excluded) items to their resolved destinations.
    /// </summary>
    public Task ImportAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct)
    {
        var toImport = items
            .Where(i => i.Status == ImportItemStatus.ReadyToImport && i.DestinationPath is not null)
            .ToList();
        return _fileExecutor.ExecuteAsync(toImport, dispatcher, ct);
    }

    private void ResolveDestinations(
        ObservableCollection<ImportItem> items,
        SettingsService.ConfigData config,
        HashSet<string> existingFilenames,
        Dictionary<string, Dictionary<string, string>> folderIndex,
        int artworkThreshold = 1,
        bool useVisualSimilarity = false)
    {
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

            if (ImportDestinationPolicy.IsDuplicateFilename(existingFilenames, item.FileName))
            {
                item.Status = ImportItemStatus.AlreadyInLibrary;
                continue;
            }

            var roots = GetRootsForCardType(item.CardType, config);
            if (roots.Count == 0) continue;

            var providerId = item.ArtworkId?.ProviderId ?? item.AuthorProviderId;
            var scope = providerId is not null ? GetProviderScope(providerId) : (Folder: "", UsesRatingFolders: true);
            var ratingFolder = scope.UsesRatingFolders ? GetRatingFolder(item.Rating, config) : "";
            var gameVersionFolder = GetGameVersionFolder(item.GameVersion, config);
            string? targetFolder = null;
            var useUnrecognizedSubfolder = item.ArtworkId is null && item.AuthorId is not null;

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

                if (targetFolder is null && item.ArtworkId is null)
                {
                    string? matchedProvider = null;
                    foreach (var root in roots)
                    {
                        foreach (var providerScope in _importProviders.Select(p => GetProviderScope(p.ProviderId)))
                        {
                            var rf = providerScope.UsesRatingFolders ? ratingFolder : "";

                            var exactKey = BuildScopeKey(root, providerScope.Folder, gameVersionFolder, rf);
                            if (folderIndex.TryGetValue(exactKey, out var exactFolders)
                                && exactFolders.TryGetValue(item.AuthorId, out var existing))
                            {
                                targetFolder = existing;
                                break;
                            }

                            foreach (var gv in GetGameVersionFolderNames(config))
                            {
                                if (gv == gameVersionFolder) continue;
                                var altKey = BuildScopeKey(root, providerScope.Folder, gv, rf);
                                if (folderIndex.TryGetValue(altKey, out var altFolders)
                                    && altFolders.ContainsKey(item.AuthorId))
                                {
                                    matchedProvider = providerScope.Folder;
                                    break;
                                }
                            }
                            if (matchedProvider is not null) break;
                        }
                        if (targetFolder is not null || matchedProvider is not null) break;
                    }

                    if (targetFolder is null && matchedProvider is not null && item.AuthorName is not null)
                    {
                        var safeName = FormatAuthorFolder(config, item.AuthorName, item.AuthorId);
                        targetFolder = BuildTargetBase(roots[0], subfolder, matchedProvider, gameVersionFolder, ratingFolder, safeName);
                    }
                }

                if (targetFolder is null && item.AuthorName is not null)
                {
                    if (providerId is not null)
                    {
                        var safeName = FormatAuthorFolder(config, item.AuthorName, item.AuthorId);
                        targetFolder = BuildTargetBase(roots[0], subfolder, scope.Folder, gameVersionFolder, ratingFolder, safeName);
                    }
                    else
                    {
                        targetFolder = BuildTargetBase(
                            roots[0],
                            subfolder,
                            config.UnknownFolderName,
                            gameVersionFolder,
                            "",
                            PathSanitizer.SanitizeFolderName(item.AuthorId));
                        useUnrecognizedSubfolder = false;
                    }
                }
            }

            targetFolder ??= BuildTargetBase(roots[0], subfolder, config.UnknownFolderName, gameVersionFolder, "", null);

            if (item.ArtworkId is not null)
            {
                var artworkSafeName = FormatArtworkFolder(config, item.Title, item.ArtworkId.Id);
                var artworkFolder = Path.Combine(targetFolder, artworkSafeName);

                var artworkFolderExists = Directory.Exists(artworkFolder);
                if (!artworkFolderExists)
                {
                    artworkCounts.TryGetValue(item.ArtworkId.Id, out var count);
                    var artworkItems = useVisualSimilarity
                        && artworkGroups.TryGetValue(item.ArtworkId.Id, out var group)
                            ? group
                            : null;
                    var visualVerdict = artworkItems is not null
                        ? CardGroupingService.ShouldGroupAsArtwork(artworkItems)
                        : null;

                    if (ImportDestinationPolicy.ShouldCreateArtworkFolder(
                            artworkFolderExists,
                            artworkThreshold,
                            count,
                            visualVerdict))
                    {
                        targetFolder = artworkFolder;
                    }
                }
                else
                    targetFolder = artworkFolder;
            }

            if (useUnrecognizedSubfolder)
                targetFolder = Path.Combine(targetFolder, UnrecognizedFolderName);

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
        return ImportDestinationPolicy.BuildTargetBase(
            root,
            subfolder,
            providerFolder,
            gameVersionFolder,
            ratingFolder,
            authorName);
    }

    private Task<(HashSet<string> ExistingFilenames, Dictionary<string, Dictionary<string, string>> FolderIndex)>
        BuildLibraryIndexAsync(SettingsService.ConfigData config, CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var filenames = BuildExistingFilenameSet(config, cancellationToken);
            var folders = BuildFolderIndex(config, cancellationToken);
            return (filenames, folders);
        }, cancellationToken);

    private HashSet<string> BuildExistingFilenameSet(
        SettingsService.ConfigData config,
        CancellationToken cancellationToken)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allRoots = config.FolderPaths
            .Concat(config.CharacterFolderPaths)
            .Concat(config.CoordinateFolderPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in allRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    set.Add(Path.GetFileName(file));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logger.LogError("Import.ScanExistingFilenames", ex, root); }
        }

        return set;
    }

    private Dictionary<string, Dictionary<string, string>> BuildFolderIndex(
        SettingsService.ConfigData config,
        CancellationToken cancellationToken)
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
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            foreach (var providerScope in providerScopes)
            {
                foreach (var gameVersionFolder in GetGameVersionFolderNames(config))
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var parsed = FindAuthorProvider(providerScope.ProviderId)?.TryParseFolderName(Path.GetFileName(dir));
                                    if (parsed is not null)
                                        authorFolders.TryAdd(parsed.Key.Id, dir);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { _logger.LogError("Import.BuildFolderIndex", ex, ratingDir); }
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
        return [.. ImportDestinationPolicy.SelectRoots(
            cardType,
            config.FolderPaths,
            config.CharacterFolderPaths,
            config.CoordinateFolderPaths)];
    }

    private static string BuildScopeKey(
        string root,
        string providerFolder,
        string gameVersionFolder,
        string ratingFolder)
        => string.Join('\u001F', root, providerFolder, gameVersionFolder, ratingFolder);

}

using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly Func<Task<SettingsService.ConfigData>> _loadConfig;
    private readonly IAppLogger _logger;
    private readonly ImportFileExecutor _fileExecutor;
    private readonly PostMetadataStore _postMetadataStore;
    private readonly LibraryFileCache _libraryFileCache;
    private readonly ImportLibraryIndexer _libraryIndexer;
    private readonly ImportResolutionCoordinator<ResolveDiagnosticResult> _resolution = new();

    public ImportService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        IReverseImageSearchProvider? reverseImageSearchProvider,
        SettingsService settingsService,
        IAppLogger logger)
        : this(importProviders, authorProviders, reverseImageSearchProvider,
            settingsService.LoadConfigAsync, logger, new PostMetadataStore(), new LibraryFileCache())
    {
    }

    internal ImportService(
        IReadOnlyList<ICardImportProvider> importProviders,
        IReadOnlyList<IFolderAuthorProvider> authorProviders,
        IReverseImageSearchProvider? reverseImageSearchProvider,
        Func<Task<SettingsService.ConfigData>> loadConfig,
        IAppLogger logger, PostMetadataStore metadataStore, LibraryFileCache libraryFileCache)
    {
        _importProviders = importProviders;
        _authorProviders = authorProviders;
        _reverseImageSearchProvider = reverseImageSearchProvider;
        _loadConfig = loadConfig;
        _postMetadataStore = metadataStore;
        _libraryFileCache = libraryFileCache;
        _logger = logger;
        _libraryIndexer = new(libraryFileCache, logger.LogError);
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

    public ArtworkId? ResolveReviewArtworkInput(string input, string? preferredProviderId)
    {
        var value = input.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return _importProviders.Select(p => p.TryParseUrl(value)).FirstOrDefault(id => id is not null);

        if (value.Contains("://", StringComparison.Ordinal) || value.Any(char.IsWhiteSpace)) return null;
        var candidates = _importProviders.Select(p => p.TryParseFilename(value))
            .Where(id => id is not null).DistinctBy(id => (id!.ProviderId, id.Id)).ToList();
        var source = ImportSourceSelection.Resolve(preferredProviderId,
            _importProviders.Select(p => p.ProviderId).ToArray(), candidates.Select(id => id!.ProviderId));
        return source is null ? null : candidates.FirstOrDefault(id => string.Equals(id!.ProviderId, source, StringComparison.OrdinalIgnoreCase))
            ?? new ArtworkId(source, value);
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
    public Task<int> AnalyzeAsync(IReadOnlyList<string> filePaths, ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher, CancellationToken ct, ImportAnalysisOptions? options = null)
        => AnalyzeAsync(filePaths, items, action => dispatcher.TryEnqueue(() => action()), ct, options);

    internal async Task<int> AnalyzeAsync(
        IReadOnlyList<string> filePaths,
        ObservableCollection<ImportItem> items,
        Func<Action, bool> enqueue,
        CancellationToken ct,
        ImportAnalysisOptions? options = null)
    {
        options ??= ImportAnalysisOptions.Default;

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
                if (options.LocalSource is { } localSource)
                {
                    // No file-name parsing for a local batch. A privately
                    // shared name can contain a digit run that would be read
                    // as a remote artwork and fetched in phase 2 below.
                    item.AuthorProviderId = LocalSourceIdentity.ProviderId;
                    item.AuthorId = localSource.AuthorId;
                    item.AuthorName = localSource.AuthorName;
                }
                else
                {
                    item.ArtworkId = TryParseFilenameAll(item.FileName);
                }

                valid.Add(item);
            }
            return (valid, rejected);
        }, ct);

        // Back on the UI thread: add valid items to the collection.
        // The first Add flips HasItems → the DropZone transitions to the queue view.
        await AwaitedUiPublication.InvokeAsync(enqueue, () =>
        {
            ct.ThrowIfCancellationRequested();
            foreach (var item in validItems) items.Add(item);
            return true;
        }, ct).ConfigureAwait(false);

        // Phase 2: deduplicate artwork IDs and fetch metadata from provider.
        // A local batch has no artwork ids, so this fetches nothing.
        var groups = validItems
            .Where(i => i.ArtworkId is not null)
            .GroupBy(i => BuildArtworkIdentityKey(i.ArtworkId!))
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

            await AwaitedUiPublication.InvokeAsync(enqueue, () =>
            {
                foreach (var item in group)
                {
                    if (!items.Contains(item)) continue;
                    if (info is not null)
                    {
                        item.FetchedArtworkInfo = info;
                        item.AuthorName = info.AuthorName;
                        item.AuthorId = info.AuthorId;
                        item.AuthorProviderId = artworkId!.ProviderId;
                        item.Title = info.Title;
                        item.Rating = info.Rating;
                        item.Tags = info.Tags;
                    }
                    else
                    {
                        item.FetchedArtworkInfo = null;
                    }
                    item.Status = ImportItemStatus.ReadyToImport;
                }
                return true;
            }, ct).ConfigureAwait(false);
        }

        // Wait for readiness publication before indexing destinations.
        await AwaitedUiPublication.InvokeAsync(enqueue, () =>
        {
            foreach (var item in validItems.Where(i => i.ArtworkId is null && items.Contains(i)))
                item.Status = ImportItemStatus.ReadyToImport;
            return true;
        }, ct).ConfigureAwait(false);

        // Initial pass still precedes fingerprint calculation; it shares the resolution pipeline.
        await ReResolveWithDetailedDiagnosticsAsync(items, enqueue, ct).ConfigureAwait(false);

        return rejectedCount;
    }

    /// <summary>
    /// Scans all library roots for author folders, returning deduplicated authors
    /// sorted by name. Used by the manual-assign picker.
    /// </summary>
    public async Task<List<(string Name, string Id, string ProviderId)>> GetKnownAuthorsAsync(CancellationToken ct)
    {
        if (_authorProviders.Count == 0) return [];

        var config = await _loadConfig().ConfigureAwait(false);

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
        _ = await ReResolveWithDetailedDiagnosticsAsync(
            items,
            dispatcher,
            ct,
            artworkSubfolderThreshold,
            useVisualSimilarity).ConfigureAwait(false);
    }

    /// <summary>
    /// Re-resolves destinations and reports the background, UI-queue, directory-I/O,
    /// and property-notification costs of the newest pass. The coordinator captures
    /// the collection on the UI dispatcher before indexing and coalesces overlapping callers.
    /// </summary>
    public Task<ResolveDiagnosticResult> ReResolveWithDetailedDiagnosticsAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct,
        int? artworkSubfolderThreshold = null,
        bool? useVisualSimilarity = null,
        bool debounce = false)
        => ReResolveWithDetailedDiagnosticsAsync(items, action => dispatcher.TryEnqueue(() => action()),
            ct, artworkSubfolderThreshold, useVisualSimilarity, debounce);

    internal Task<ResolveDiagnosticResult> ReResolveWithDetailedDiagnosticsAsync(
        ObservableCollection<ImportItem> items,
        Func<Action, bool> enqueue,
        CancellationToken ct,
        int? artworkSubfolderThreshold = null,
        bool? useVisualSimilarity = null,
        bool debounce = false)
        => _resolution.RequestAsync(async token =>
        {
            var snapshot = await AwaitedUiPublication.InvokeAsync(enqueue, () =>
            {
                var capturedItems = items.ToArray();
                var sources = capturedItems.Select(item =>
                    new ImportLibrarySource(item.SourceFilePath, item.FileName)).ToArray();
                return (Items: capturedItems, Sources: sources);
            }, token).ConfigureAwait(false);
            var config = await _loadConfig().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var backgroundStopwatch = Stopwatch.StartNew();
            var libraryIndex = await _libraryIndexer.BuildAsync(CreateLibraryIndexRequest(config, snapshot.Sources), token).ConfigureAwait(false);
            backgroundStopwatch.Stop();
            int threshold = artworkSubfolderThreshold ?? config.ArtworkSubfolderThreshold;
            bool visual = useVisualSimilarity ?? config.UseVisualSimilarity;
            var queueStopwatch = Stopwatch.StartNew();
            return () =>
            {
                queueStopwatch.Stop();
                var diagnostics = new ResolveDiagnosticCollector();
                // Never apply an old snapshot's index to newly added or removed items.
                var current = new ObservableCollection<ImportItem>(snapshot.Items.Where(items.Contains));
                ResolveDestinations(current, config, libraryIndex.IdenticalSourcePaths,
                    libraryIndex.FolderIndex, threshold, visual, diagnostics);
                return diagnostics.ToResult(backgroundStopwatch.ElapsedMilliseconds,
                    queueStopwatch.ElapsedMilliseconds, libraryIndex);
            };
        }, enqueue, debounce, ct);

    public void CancelPendingResolution() => _resolution.CancelAll();

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
        var preflightedItems = await PrepareArtworkPromotionsAsync(
            toImport,
            dispatcher,
            ct).ConfigureAwait(false);
        await _fileExecutor.ExecuteAsync(
            preflightedItems,
            dispatcher,
            ct,
            SaveFetchedMetadataAsync);
    }

    /// <summary>
    /// Registers files committed by the transaction executor, avoiding a full
    /// library scan the next time an import workspace is re-resolved.
    /// </summary>
    public void RegisterCommittedLibraryFiles(IEnumerable<string> destinationPaths)
        => _libraryFileCache.RegisterCommittedFiles(destinationPaths);

    /// <summary>
    /// Causes the following resolution to rebuild its filename index from disk.
    /// </summary>
    public void InvalidateLibraryFileCache() => _libraryFileCache.Invalidate();

    private void ResolveDestinations(
        ObservableCollection<ImportItem> items,
        SettingsService.ConfigData config,
        IReadOnlySet<string> identicalSourcePaths,
        Dictionary<string, Dictionary<string, string>> folderIndex,
        int artworkThreshold = 1,
        bool useVisualSimilarity = false,
        ResolveDiagnosticCollector? diagnostics = null)
    {
        var subfolder = config.ImportSubfolder.Trim();

        foreach (var item in items)
        {
            if (item.Status != ImportItemStatus.ReadyToImport || item.CardType == CardType.NotACard)
                continue;

            SetAuthorDirectoryPath(item, null, diagnostics);
            if (identicalSourcePaths.Contains(item.SourceFilePath))
            {
                SetStatus(item, ImportItemStatus.AlreadyInLibrary, diagnostics);
                continue;
            }

            var roots = GetRootsForCardType(item.CardType, config);
            if (roots.Count == 0) continue;

            var providerId = item.ArtworkId?.ProviderId ?? item.AuthorProviderId;
            var scope = providerId is not null ? GetProviderScope(providerId) : (Folder: "", UsesRatingFolders: true);
            var ratingFolder = scope.UsesRatingFolders ? GetRatingFolder(item.Rating, config) : "";
            var gameVersionFolder = GetGameVersionFolder(item.GameVersion, config);
            string? targetFolder = null;
            var useUnrecognizedSubfolder = ImportDestinationPolicy.UsesUnrecognizedSink(
                hasArtwork: item.ArtworkId is not null,
                hasAuthor: item.AuthorId is not null,
                item.AuthorProviderId);

            if (item.AuthorId is not null)
            {
                foreach (var root in roots)
                {
                    var scopeKey = ImportLibraryIndexer.BuildScopeKey(root, scope.Folder, gameVersionFolder, ratingFolder);
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

                            var exactKey = ImportLibraryIndexer.BuildScopeKey(root, providerScope.Folder, gameVersionFolder, rf);
                            if (folderIndex.TryGetValue(exactKey, out var exactFolders)
                                && exactFolders.TryGetValue(item.AuthorId, out var existing))
                            {
                                targetFolder = existing;
                                break;
                            }

                            foreach (var gv in GetGameVersionFolderNames(config))
                            {
                                if (gv == gameVersionFolder) continue;
                                var altKey = ImportLibraryIndexer.BuildScopeKey(root, providerScope.Folder, gv, rf);
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
            SetAuthorDirectoryPath(item, item.ArtworkId is not null ? targetFolder : null, diagnostics);

            if (useUnrecognizedSubfolder)
                targetFolder = Path.Combine(targetFolder, GalleryGrouping.UnrecognizedFolderName);

            SetDestinationPath(item, Path.Combine(targetFolder, item.FileName), diagnostics);
        }

        ResolveArtworkDestinations(items, config, artworkThreshold, useVisualSimilarity, diagnostics);
    }

    private Task<List<ImportItem>> PrepareArtworkPromotionsAsync(
        IReadOnlyList<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken cancellationToken)
        => Task.Run(() =>
        {
            var blockedItems = new HashSet<ImportItem>();
            var promotionGroups = items
                .Where(IsArtworkFolderImport)
                .GroupBy(
                    item => BuildArtworkGroupKey(
                        item.AuthorDirectoryPath!,
                        item.ArtworkId!),
                    StringComparer.OrdinalIgnoreCase);

            foreach (var group in promotionGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var artworkItems = group.ToList();
                var first = artworkItems[0];
                var artworkDirectory = Path.GetDirectoryName(first.DestinationPath!)!;

                try
                {
                    var existingRootFiles = FindRootArtworkFiles(
                        first.AuthorDirectoryPath!,
                        first.ArtworkId!,
                        cancellationToken);
                    var result = ArtworkPromotionService.PreflightAndPromote(
                        existingRootFiles,
                        artworkItems.Select(item => item.SourceFilePath).ToList(),
                        artworkDirectory,
                        cancellationToken);
                    if (result.Succeeded)
                        continue;

                    foreach (var item in artworkItems)
                        blockedItems.Add(item);
                    dispatcher.TryEnqueue(() =>
                    {
                        foreach (var item in artworkItems)
                        {
                            item.Status = ImportItemStatus.Skipped;
                            item.ErrorMessage = $"Conflicting artwork file: {result.CollisionFileName}";
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    foreach (var item in artworkItems)
                        blockedItems.Add(item);
                    _logger.LogError("Import.PromoteArtwork", ex, artworkDirectory);
                    dispatcher.TryEnqueue(() =>
                    {
                        foreach (var item in artworkItems)
                        {
                            item.Status = ImportItemStatus.Failed;
                            item.ErrorMessage = ex.Message;
                        }
                    });
                }
            }

            return items.Where(item => !blockedItems.Contains(item)).ToList();
        }, cancellationToken);

    private async Task SaveFetchedMetadataAsync(
        ImportItem item,
        CancellationToken cancellationToken)
    {
        var info = item.FetchedArtworkInfo;
        var authorDirectory = item.AuthorDirectoryPath;
        if (info is null || string.IsNullOrWhiteSpace(authorDirectory))
            return;

        try
        {
            var localFileNames = string.IsNullOrWhiteSpace(item.DestinationPath)
                ? []
                : new[] { Path.GetFileName(item.DestinationPath) };
            await _postMetadataStore.WriteAsync(
                authorDirectory,
                PostMetadataMapper.ToDocument(info, localFileNames),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Import.WritePostMetadata",
                ex,
                _postMetadataStore.GetSidecarPath(
                    authorDirectory,
                    info.ArtworkId.ProviderId,
                    info.ArtworkId.Id));
        }
    }

    private void ResolveArtworkDestinations(
        IReadOnlyList<ImportItem> items,
        SettingsService.ConfigData config,
        int artworkThreshold,
        bool useVisualSimilarity,
        ResolveDiagnosticCollector? diagnostics = null)
    {
        var artworkGroups = items
            .Where(item => item.Status == ImportItemStatus.ReadyToImport
                && item.ArtworkId is not null
                && item.AuthorDirectoryPath is not null
                && item.DestinationPath is not null)
            .GroupBy(
                item => BuildArtworkGroupKey(
                    item.AuthorDirectoryPath!,
                    item.ArtworkId!),
                StringComparer.OrdinalIgnoreCase);

        foreach (var group in artworkGroups)
        {
            var artworkItems = group.ToList();
            var first = artworkItems[0];
            var authorDirectory = first.AuthorDirectoryPath!;
            var artworkId = first.ArtworkId!;

            try
            {
                var existingArtworkDirectory = MeasureArtworkDirectoryLookup(
                    diagnostics,
                    () => FindArtworkDirectory(authorDirectory, artworkId));
                var existingRootFiles = MeasureArtworkDirectoryLookup(
                    diagnostics,
                    () => FindRootArtworkFiles(
                        authorDirectory,
                        artworkId,
                        CancellationToken.None));

                bool shouldUseArtworkDirectory;
                if (existingArtworkDirectory is not null)
                {
                    shouldUseArtworkDirectory = true;
                }
                else if (existingRootFiles.Count > 0)
                {
                    shouldUseArtworkDirectory = ImportDestinationPolicy.ShouldCreateArtworkFolder(
                        alreadyExists: false,
                        artworkThreshold,
                        existingRootFiles.Count + artworkItems.Count,
                        visualSimilarityVerdict: null);
                }
                else
                {
                    var visualVerdict = useVisualSimilarity
                        ? CardGroupingService.ShouldGroupAsArtwork(artworkItems)
                        : null;
                    shouldUseArtworkDirectory = ImportDestinationPolicy.ShouldCreateArtworkFolder(
                        alreadyExists: false,
                        artworkThreshold,
                        artworkItems.Count,
                        visualVerdict);
                }

                if (!shouldUseArtworkDirectory)
                    continue;

                var artworkDirectory = existingArtworkDirectory
                    ?? Path.Combine(
                        authorDirectory,
                        FormatArtworkFolder(config, first.Title, artworkId.Id));
                foreach (var item in artworkItems)
                    SetDestinationPath(item, Path.Combine(artworkDirectory, item.FileName), diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError("Import.ResolveArtworkDirectory", ex, authorDirectory);
            }
        }
    }

    private static T MeasureArtworkDirectoryLookup<T>(
        ResolveDiagnosticCollector? diagnostics,
        Func<T> operation)
        => diagnostics is null
            ? operation()
            : diagnostics.MeasureArtworkDirectoryLookup(operation);

    private static void SetAuthorDirectoryPath(
        ImportItem item,
        string? value,
        ResolveDiagnosticCollector? diagnostics)
    {
        if (string.Equals(item.AuthorDirectoryPath, value, StringComparison.Ordinal))
            return;

        if (diagnostics is null)
        {
            item.AuthorDirectoryPath = value;
            return;
        }

        diagnostics.MeasurePropertyAssignment(() => item.AuthorDirectoryPath = value);
    }

    private static void SetDestinationPath(
        ImportItem item,
        string? value,
        ResolveDiagnosticCollector? diagnostics)
    {
        if (string.Equals(item.DestinationPath, value, StringComparison.Ordinal))
            return;

        if (diagnostics is null)
        {
            item.DestinationPath = value;
            return;
        }

        diagnostics.MeasurePropertyAssignment(() => item.DestinationPath = value);
        diagnostics.DestinationPathAssignments++;
    }

    private static void SetStatus(
        ImportItem item,
        ImportItemStatus value,
        ResolveDiagnosticCollector? diagnostics)
    {
        if (item.Status == value)
            return;

        if (diagnostics is null)
        {
            item.Status = value;
            return;
        }

        diagnostics.MeasurePropertyAssignment(() => item.Status = value);
        if (value == ImportItemStatus.AlreadyInLibrary)
            diagnostics.StatusTransitionsToAlreadyInLibrary++;
    }

    private sealed class ResolveDiagnosticCollector
    {
        private long _artworkDirectoryLookupElapsedTicks;
        private long _propertyAssignmentElapsedTicks;

        public int DestinationPathAssignments { get; set; }
        public int StatusTransitionsToAlreadyInLibrary { get; set; }

        public T MeasureArtworkDirectoryLookup<T>(Func<T> operation)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                return operation();
            }
            finally
            {
                _artworkDirectoryLookupElapsedTicks += Stopwatch.GetTimestamp() - startedAt;
            }
        }

        public void MeasurePropertyAssignment(Action operation)
        {
            var startedAt = Stopwatch.GetTimestamp();
            try
            {
                operation();
            }
            finally
            {
                _propertyAssignmentElapsedTicks += Stopwatch.GetTimestamp() - startedAt;
            }
        }

        public ResolveDiagnosticResult ToResult(
            long backgroundIndexElapsedMs,
            long uiQueueDelayMs,
            ImportLibraryIndexResult libraryIndex)
            => new(
                backgroundIndexElapsedMs,
                libraryIndex.LibraryScanAndCandidateIndexElapsedMs,
                libraryIndex.ByteComparisonElapsedMs,
                libraryIndex.FolderIndexElapsedMs,
                libraryIndex.TotalCandidatesCompared,
                libraryIndex.ActualIdenticalDuplicates,
                libraryIndex.ActualContentMismatches,
                uiQueueDelayMs,
                (long)Stopwatch.GetElapsedTime(0, _artworkDirectoryLookupElapsedTicks).TotalMilliseconds,
                (long)Stopwatch.GetElapsedTime(0, _propertyAssignmentElapsedTicks).TotalMilliseconds,
                DestinationPathAssignments,
                StatusTransitionsToAlreadyInLibrary);
    }

    private string? FindArtworkDirectory(string authorDirectory, ArtworkId artworkId)
    {
        if (!Directory.Exists(authorDirectory))
            return null;

        var provider = FindProvider(artworkId.ProviderId);
        if (provider is null)
            return null;

        var candidates = Directory.EnumerateDirectories(authorDirectory)
            .Where(path => !Path.GetFileName(path).Equals(
                PostMetadataStore.MetadataDirectoryName,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        return ArtworkDirectoryLookup.FindFirst(candidates, artworkId.ProviderId, artworkId.Id,
            name =>
            {
                var parsed = provider.TryParseArtworkFolderName(name);
                return parsed is null ? null : (parsed.ProviderId, parsed.Id);
            });
    }

    private List<string> FindRootArtworkFiles(
        string authorDirectory,
        ArtworkId artworkId,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(authorDirectory))
            return [];

        var provider = FindProvider(artworkId.ProviderId);
        if (provider is null)
            return [];

        var result = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     authorDirectory,
                     "*.png",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = provider.TryParseFilename(Path.GetFileName(path));
            if (parsed is not null
                && parsed.ProviderId.Equals(artworkId.ProviderId, StringComparison.OrdinalIgnoreCase)
                && parsed.Id.Equals(artworkId.Id, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static bool IsArtworkFolderImport(ImportItem item)
        => item.ArtworkId is not null
            && item.AuthorDirectoryPath is not null
            && item.DestinationPath is not null
            && !Path.GetDirectoryName(item.DestinationPath)!.Equals(
                item.AuthorDirectoryPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);

    private static string BuildArtworkGroupKey(
        string authorDirectory,
        ArtworkId artworkId)
        => $"{Path.GetFullPath(authorDirectory)}\u001F{BuildArtworkIdentityKey(artworkId)}";

    private static string BuildArtworkIdentityKey(ArtworkId artworkId)
        => $"{artworkId.ProviderId}\u001F{artworkId.Id}";

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

    private ImportLibraryIndexRequest CreateLibraryIndexRequest(
        SettingsService.ConfigData config, IReadOnlyList<ImportLibrarySource> sources)
    {
        var roots = config.FolderPaths.Concat(config.CharacterFolderPaths)
            .Concat(config.CoordinateFolderPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scopes = _importProviders.Select(provider =>
        {
            var scope = GetProviderScope(provider.ProviderId);
            var authorProvider = FindAuthorProvider(provider.ProviderId);
            return new ImportLibraryProviderScope(scope.Folder,
                scope.UsesRatingFolders ? GetRatingFolderNames(config) : [""],
                name => authorProvider?.TryParseFolderName(name)?.Key.Id);
        }).ToArray();
        return new(sources, roots, config.ImportSubfolder, GetGameVersionFolderNames(config), scopes);
    }

    private static List<string> GetRootsForCardType(CardType cardType, SettingsService.ConfigData config)
    {
        return [.. ImportDestinationPolicy.SelectRoots(
            cardType,
            config.FolderPaths,
            config.CharacterFolderPaths,
            config.CoordinateFolderPaths)];
    }


}

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

    /// <summary>
    /// The rating folders an author folder can sit under, for finding existing
    /// folders. "" is the three-level layout, where authors sit directly under
    /// the provider (or game) folder. Distinct, so a rating folder name left
    /// blank in the settings does not scan the same folder twice.
    /// </summary>
    private static string[] GetRatingFolderNames(SettingsService.ConfigData config) =>
        [.. new[] { config.GFolderName, config.R18FolderName, config.R18GFolderName, "" }
            .Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

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
    private readonly FeedHandoffManifest _feedHandoff = new();

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

    /// <summary>
    /// Applies the post and creator provenance KoikatsuSceneFeed recorded for this card. The manifest remains useful
    /// without a Gallery plugin: the card can still be organized under its provider, post, and creator. When a plugin
    /// is installed, its URL parser may canonicalize the post id and phase 2 can enrich the remaining metadata.
    /// </summary>
    private bool TryApplyFeedHandoff(ImportItem item)
    {
        if (_feedHandoff.Find(item.SourceFilePath) is not { } source)
            return false;

        var provider = FindProvider(source.ProviderId);
        var parsed = source.PostUrl is { } url && provider is not null ? provider.TryParseUrl(url) : null;
        item.ArtworkId = parsed is not null
                         && string.Equals(parsed.ProviderId, source.ProviderId, StringComparison.OrdinalIgnoreCase)
            ? parsed
            : new ArtworkId(source.ProviderId, source.PostId);
        item.AuthorProviderId = source.ProviderId;
        item.AuthorId = source.CreatorId;
        item.AuthorName = source.CreatorName ?? source.CreatorId;
        return true;
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
            MaxDegreeOfParallelism = AppLimits.IoWorkers,
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
                    // Feed's byte-hash provenance is stronger than a filename heuristic. This matters for archive
                    // entries whose arbitrary names can look like another provider's artwork id.
                    if (!TryApplyFeedHandoff(item))
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
                    libraryIndex.FolderIndex, threshold, visual, diagnostics, libraryIndex.ExistingLibraryPaths);
                var result = diagnostics.ToResult(backgroundStopwatch.ElapsedMilliseconds,
                    queueStopwatch.ElapsedMilliseconds, libraryIndex);
                RecordSlowResolution(result, snapshot.Sources.Length);
                return result;
            };
        }, enqueue, debounce, ct);

    /// <summary>How long a resolution pass may take before it is worth recording.</summary>
    private static readonly int SlowResolutionMs = (int)AppTiming.SlowResolution.TotalMilliseconds;

    /// <summary>
    /// Records the timing of a resolution pass that was slow, or that had to
    /// rebuild the library index.
    /// </summary>
    /// <remarks>
    /// The breakdown has always been collected and always been discarded, which
    /// left "the import sometimes takes a long time, and not in proportion to
    /// the batch" impossible to answer. The library index is the suspect: it is
    /// one snapshot keyed by the root set, so the first import after launch —
    /// and any import after the index is invalidated — walks every library root
    /// recursively, however few cards are being imported.
    ///
    /// Only slow or rebuilding passes are recorded. The review page re-resolves
    /// on every edit, and logging each one would bury everything else.
    ///
    /// The decision is made here, on the UI thread that commits the pass, but
    /// the write is handed to the thread pool: the log is an append under a
    /// global lock, and a diagnostic must not add blocking file I/O to the
    /// publication the user is waiting on.
    /// </remarks>
    private void RecordSlowResolution(ResolveDiagnosticResult result, int sourceCount)
    {
        var rebuiltIndex = result.BgLibraryScanAndCandidateIndexElapsedMs > 0;
        var total = result.BackgroundIndexElapsedMs + result.UiQueueDelayMs
            + result.UiArtworkDirectoryLookupElapsedMs + result.UiPropertyAssignmentElapsedMs;
        if (!rebuiltIndex && total < SlowResolutionMs)
            return;

        var message =
                $"cards={sourceCount} total={total}ms background={result.BackgroundIndexElapsedMs}ms "
                + $"libraryScan={result.BgLibraryScanAndCandidateIndexElapsedMs}ms"
                + $"{(rebuiltIndex ? " (rebuilt)" : "")} "
                + $"byteCompare={result.BgByteComparisonElapsedMs}ms "
                + $"folderIndex={result.BgFolderIndexElapsedMs}ms "
                + $"uiQueue={result.UiQueueDelayMs}ms "
                + $"artworkLookup={result.UiArtworkDirectoryLookupElapsedMs}ms "
                + $"assign={result.UiPropertyAssignmentElapsedMs}ms "
                + $"compared={result.TotalCandidatesCompared} "
                + $"duplicates={result.ActualIdenticalDuplicates} "
                + $"mismatches={result.ActualContentMismatches}";

        _ = Task.Run(() => _logger.LogError("Import.SlowResolution", new TimeoutException(message)));
    }

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
    /// <summary>
    /// Puts a link at <paramref name="destination"/> to the library's own copy of the card,
    /// binds the name to the post as an import would, and deletes the source as a consumed
    /// duplicate. Returns why it failed, or null.
    /// </summary>
    internal async Task<string?> LinkToExistingAsync(ImportItem item, string destination, string existing, bool symbolic)
    {
        var document = PostMetadataMapper.ToImportDocument(item, []);
        var authorDirectory = item.AuthorDirectoryPath;
        var source = item.SourceFilePath;
        try
        {
            await Task.Run(() =>
            {
                if (symbolic) FileLinks.CreateSymbolicLink(destination, existing);
                else FileLinks.CreateHardLink(destination, existing);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }

        try
        {
            if (document is not null && authorDirectory is not null)
                await _postMetadataStore.WriteAsync(authorDirectory,
                    document with { LocalFileNames = [Path.GetFileName(destination)] }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Import.LinkSidecar", ex, destination);
        }

        RegisterCommittedLibraryFiles([destination]);
        try { await Task.Run(() => File.Delete(source)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Import.LinkDeleteSource", ex, source);
        }
        return null;
    }

    public void RegisterCommittedLibraryFiles(IEnumerable<string> destinationPaths)
    {
        _libraryFileCache.RegisterCommittedFiles(destinationPaths);
        ForgetLibraryNames();
    }

    /// <summary>
    /// Causes the following resolution to rebuild its filename index from disk.
    /// </summary>
    public void InvalidateLibraryFileCache()
    {
        _libraryFileCache.Invalidate();
        ForgetLibraryNames();
    }

    // The library's files by name across every configured root, for "does the library
    // already hold this file?". Rebuilt when the roots change or an import commits.
    private readonly object _libraryNamesGate = new();
    private (string Key, LibraryNameIndex Index)? _libraryNames;

    private void ForgetLibraryNames()
    {
        lock (_libraryNamesGate) _libraryNames = null;
    }

    /// <summary>
    /// A question the import page can ask of the library: is there a file of this name,
    /// and of this size when one is given? The first call scans the library.
    /// </summary>
    public async Task<Func<string, long?, bool>> GetLibraryLookupAsync(CancellationToken ct)
    {
        var config = await _loadConfig().ConfigureAwait(false);
        var roots = (config.FolderPaths ?? [])
            .Concat(config.CharacterFolderPaths ?? [])
            .Concat(config.CoordinateFolderPaths ?? [])
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var key = string.Join("\n", roots);

        lock (_libraryNamesGate)
        {
            if (_libraryNames is { } cached && cached.Key == key) return cached.Index.Has;
        }

        var index = await Task.Run(
            () => LibraryNameIndex.Build(roots, _logger.LogError, ct), ct).ConfigureAwait(false);
        lock (_libraryNamesGate) _libraryNames = (key, index);
        return index.Has;
    }

    private void ResolveDestinations(
        ObservableCollection<ImportItem> items,
        SettingsService.ConfigData config,
        IReadOnlySet<string> identicalSourcePaths,
        Dictionary<string, Dictionary<string, string>> folderIndex,
        int artworkThreshold = 1,
        bool useVisualSimilarity = false,
        ResolveDiagnosticCollector? diagnostics = null,
        IReadOnlyDictionary<string, string>? existingLibraryPaths = null)
    {
        // Invariant across the whole pass: materialized once instead of being
        // re-evaluated per root inside the fallback lookup, for every item.
        var providerScopes = _importProviders.Select(p => GetProviderScope(p.ProviderId)).ToArray();

        foreach (var item in items)
        {
            if (item.Status != ImportItemStatus.ReadyToImport || item.CardType == CardType.NotACard)
                continue;

            SetAuthorDirectoryPath(item, null, diagnostics);
            if (identicalSourcePaths.Contains(item.SourceFilePath))
            {
                // Still given the destination it would have had: when that is not where the
                // library keeps it, the user can link the card there or copy it (see ImportConflict).
                SetStatus(item, ImportItemStatus.AlreadyInLibrary, diagnostics);
                item.ExistingLibraryPath = existingLibraryPaths?.GetValueOrDefault(item.SourceFilePath);
            }

            ResolveItemDestination(item, config, folderIndex, providerScopes, diagnostics);
        }

        ResolveArtworkDestinations(items, config, artworkThreshold, useVisualSimilarity, diagnostics);
    }

    /// <summary>
    /// Sets one card's <see cref="ImportItem.DestinationPath"/> and
    /// <see cref="ImportItem.AuthorDirectoryPath"/> from its provider, author,
    /// rating and game, before any artwork subfolder. Everything that decides
    /// the folder is <see cref="ImportDestinationPolicy.ResolveTarget"/>; this
    /// only reads the settings and providers it needs.
    /// </summary>
    private void ResolveItemDestination(
        ImportItem item,
        SettingsService.ConfigData config,
        Dictionary<string, Dictionary<string, string>> folderIndex,
        IReadOnlyList<(string Folder, bool UsesRatingFolders)> providerScopes,
        ResolveDiagnosticCollector? diagnostics)
    {
        var providerId = item.ArtworkId?.ProviderId ?? item.AuthorProviderId;
        var target = ImportDestinationPolicy.ResolveTarget(new ImportTargetInput(
            Roots: GetRootsForCardType(item.CardType, config),
            ImportSubfolder: config.ImportSubfolder,
            UnknownFolderName: config.UnknownFolderName,
            GameVersionFolder: GetGameVersionFolder(item.GameVersion, config),
            GameVersionFolderNames: GetGameVersionFolderNames(config),
            Scope: providerId is not null ? GetProviderScope(providerId) : (Folder: "", UsesRatingFolders: true),
            RatingFolder: GetRatingFolder(item.Rating, config),
            ProviderScopes: providerScopes,
            HasArtwork: item.ArtworkId is not null,
            ProviderId: providerId,
            AuthorId: item.AuthorId,
            AuthorName: item.AuthorName,
            AuthorProviderId: item.AuthorProviderId,
            FormatAuthorFolder: (name, id) => FormatAuthorFolder(config, name, id)), folderIndex);
        if (target is null) return;

        SetAuthorDirectoryPath(item, target.AuthorDirectory, diagnostics);
        SetDestinationPath(item, Path.Combine(target.Folder, item.FileName), diagnostics);
    }

    /// <summary>
    /// Where the import rules put <paramref name="item"/>, artwork subfolder
    /// included, for a card that is already in the library: the same path an
    /// import would choose, without the "already in library" and duplicate
    /// classification, which would otherwise mark the card as its own copy.
    /// Sets the item's destination and author directory; returns the
    /// destination, or null when no library root takes the card's type.
    /// </summary>
    internal async Task<string?> ResolveLibraryCardDestinationAsync(ImportItem item, CancellationToken ct)
    {
        var config = await _loadConfig().ConfigureAwait(false);
        var libraryIndex = await _libraryIndexer.BuildAsync(CreateLibraryIndexRequest(config, []), ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        item.Status = ImportItemStatus.ReadyToImport;
        item.DestinationPath = null;
        item.AuthorDirectoryPath = null;
        var providerScopes = _importProviders.Select(p => GetProviderScope(p.ProviderId)).ToArray();
        ResolveItemDestination(item, config, libraryIndex.FolderIndex, providerScopes, null);
        if (item.DestinationPath is null) return null;
        // A batch of one: the artwork folder is used when it already exists
        // or when the post's other files in the author folder pass the threshold.
        ResolveArtworkDestinations([item], config, config.ArtworkSubfolderThreshold, useVisualSimilarity: false);
        return item.DestinationPath;
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

    /// <summary>
    /// Writes the post sidecar for a library card given its source later, the
    /// way an import writes it, but reporting a failure instead of logging it
    /// away: the caller has already moved the card and must say the sidecar
    /// is missing. Throws when it could not be written.
    /// </summary>
    internal async Task WriteLibraryCardSidecarAsync(ImportItem item, CancellationToken cancellationToken)
    {
        var info = item.FetchedArtworkInfo
            ?? throw new InvalidOperationException("The card has no fetched post.");
        var authorDirectory = item.AuthorDirectoryPath;
        if (string.IsNullOrWhiteSpace(authorDirectory) || string.IsNullOrWhiteSpace(item.DestinationPath))
            throw new InvalidOperationException("The card has no author folder.");
        var document = PostMetadataMapper.ToImportDocument(item, [Path.GetFileName(item.DestinationPath)])
            ?? throw new InvalidOperationException("The post could not be turned into a sidecar.");
        try
        {
            // False only means the sidecar already said all this; not a failure.
            await _postMetadataStore.WriteAsync(authorDirectory, document, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Reidentify.WritePostMetadata", ex,
                _postMetadataStore.GetSidecarPath(authorDirectory, info.ArtworkId.ProviderId, info.ArtworkId.Id));
            throw;
        }
    }

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
                PostMetadataMapper.ToImportDocument(item, localFileNames)!,
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
                PathComparison.Comparison);

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

    /// <summary>
    /// What the library layout migration needs, from the same sources the
    /// folder index uses: the roots, the import folder, every plugin's folder
    /// (plus Unknown), the game and rating folder names, and each plugin's own
    /// rule for reading an author id from a folder name.
    /// </summary>
    internal async Task<LayoutMigrationOptions> BuildLayoutMigrationOptionsAsync()
    {
        var config = await _loadConfig().ConfigureAwait(false);
        var roots = config.FolderPaths.Concat(config.CharacterFolderPaths).Concat(config.CoordinateFolderPaths)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var scopes = _importProviders
            .Select(provider => (GetProviderScope(provider.ProviderId).Folder, Author: FindAuthorProvider(provider.ProviderId)))
            .Where(scope => scope.Folder.Length > 0)
            .ToList();
        string[] providerFolders = [.. scopes.Select(scope => scope.Folder)
            .Append(config.UnknownFolderName.Trim())
            .Where(folder => folder.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        (string, int)[] ratings = [.. new[]
            {
                (config.GFolderName, SceneRating.General),
                (config.R18FolderName, SceneRating.R18),
                (config.R18GFolderName, SceneRating.R18G),
            }
            .Select(r => (r.Item1.Trim(), r.Item2))
            .Where(r => r.Item1.Length > 0)];
        return new LayoutMigrationOptions(
            roots,
            config.ImportSubfolder.Trim(),
            providerFolders,
            config.UnknownFolderName.Trim(),
            [.. GetGameVersionFolderNames(config).Where(name => !string.IsNullOrWhiteSpace(name))],
            ratings,
            (folder, name) => scopes
                .Where(scope => string.Equals(scope.Folder, folder, StringComparison.OrdinalIgnoreCase))
                .Select(scope => scope.Author?.TryParseFolderName(name)?.Key.Id)
                .FirstOrDefault(id => id is not null));
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

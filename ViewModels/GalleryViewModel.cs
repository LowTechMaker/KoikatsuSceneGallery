using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public enum GameFilterOption
{
    All,
    Koikatsu,
    KoikatsuSunshine,
    Unknown
}

public partial class GalleryViewModel : GalleryViewModelBase, IDisposable
{
    private readonly SceneCardService _sceneCardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly SceneMetadataService _metadataService;
    private readonly SceneCardCacheService _cardCacheService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly PluginService _pluginService;
    private readonly SceneRatingResolver _ratingResolver;

    /// <summary>Post titles and tags for the home page, read with the ratings.</summary>
    internal SceneRatingResolver RatingResolver => _ratingResolver;
    private readonly IAppLogger _logger;

    public ObservableCollection<SceneCard> Cards { get; }

    /// <summary>
    /// The app-wide rating filter as it applies now
    /// (<see cref="SettingsViewModel.EffectiveRatingFilter"/>), pushed in by the
    /// main window the way the browse origin is; never set from a page.
    /// </summary>
    [ObservableProperty]
    public partial RatingSet RatingFilter { get; set; } = RatingSet.All;

    /// <summary>
    /// False from the start of a load until the platform and user ratings of
    /// that load have been applied. Until then a card's rating is its folder or
    /// filed rating, which the sidecars may still change.
    /// </summary>
    public bool RatingsSettled { get; private set; }

    /// <summary>Raised on the UI thread when card ratings may have changed (settled after a load, a user or filed rating).</summary>
    public event Action? RatingsChanged;

    // Cards the scan of this load added or replaced: their platform rating is only
    // known once the ratings are read again after the scan. UI thread only.
    private readonly HashSet<string> _ratingPending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the card's rating is final for this load: the ratings were read,
    /// and the card is not one the scan brought in after that read.
    /// </summary>
    public bool IsRatingSettled(SceneCard card) => RatingsSettled && !_ratingPending.Contains(card.FilePath);

    // This load put the whole card cache on screen; the scan has changed the list since.
    private bool _cacheShown;
    private bool _scanChanged;

    /// <summary>
    /// Whether <see cref="Cards"/> is a whole library: the load has finished, or
    /// it showed the whole cache (the library as of the last run) and the scan
    /// has changed nothing yet. What must not act on a half-loaded list (the
    /// home page's themes) waits for this.
    /// </summary>
    public bool CardsComplete => !IsLoading || (_cacheShown && !_scanChanged);

    /// <summary>Raised on the UI thread once a load has put the whole card cache on screen, before the scan.</summary>
    public event Action? CachedCardsShown;

    private const string TimingLibrary = "scenes";

    /// <summary>
    /// A card from its cache entry. A thumbnail found missing is left off, so it
    /// is made again when the card is shown.
    /// </summary>
    private static SceneCard FromCache(CachedCardEntry entry, HashSet<string> staleThumbnails)
    {
        var card = new SceneCard
        {
            FilePath = entry.FilePath,
            FileSize = entry.FileSize,
            DateModified = new DateTime(entry.DateModifiedTicks),
            DateCreated = new DateTime(entry.DateCreatedTicks),
            Width = entry.Width,
            Height = entry.Height,
        };
        if (entry.ThumbnailPath is not null && !staleThumbnails.Contains(entry.FilePath))
            card.ThumbnailPath = entry.ThumbnailPath;
        return card;
    }

    /// <summary>Whether a scene card passes the rating filter: the one rule, with the card's resolved rating.</summary>
    public bool RatingPasses(SceneCard card) => RatingFilterRule.Passes(RatingFilter, card.Rating);

    // Scoped browses (author, tag and post pages) only asked the search before;
    // the rating filter is app-wide, so they ask it too.
    public override bool MatchesBrowseCard(CardBase card, IReadOnlyList<string> keywords)
        => base.MatchesBrowseCard(card, keywords) && (card is not SceneCard scene || RatingPasses(scene));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    [NotifyPropertyChangedFor(nameof(IsNotParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;
    public bool IsNotParsingMetadata => PendingMetadataCount == 0;

    [ObservableProperty]
    public partial bool ShowMetadataFilters { get; set; }

    [ObservableProperty]
    public partial GameFilterOption GameFilter { get; set; } = GameFilterOption.All;

    private bool HasMetadataFilter =>
        GameFilter != GameFilterOption.All;

    private HashSet<string> _r18FolderNames = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _r18GFolderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly GalleryMetadataSession<SceneCard, SceneMetadata> _metadata;
    private bool _pluginAnalysisEnabled;

    public event Action<string>? CardRemovedNotification;

    internal GalleryViewModel(SceneCardService sceneCardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, SceneMetadataService metadataService, SceneCardCacheService cardCacheService, SettingsViewModel settingsViewModel, PluginService pluginService, ThumbnailPriorityScheduler thumbnailScheduler, SceneRatingResolver ratingResolver, IAppLogger logger)
        : base(new ObservableCollection<SceneCard>(), thumbnailScheduler)
    {
        Cards = (ObservableCollection<SceneCard>)_cardsSource;
        _sceneCardService = sceneCardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _cardCacheService = cardCacheService;
        _settingsViewModel = settingsViewModel;
        _pluginService = pluginService;
        _ratingResolver = ratingResolver;
        _logger = logger;
        _metadata = new(
            new MetadataScanCoordinator<SceneCard, SceneMetadata>(
                card => card.MetadataLoaded,
                _metadataService.TryGetCached,
                _metadataService.ParseAndCache,
                ApplyMetadata,
                action => _dispatcherQueue.TryEnqueue(() => action()),
                (card, error) => _logger.LogError(
                    error is OperationCanceledException ? "Gallery.ParseMetadataCanceled" : "Gallery.ParseMetadata",
                    error, card.FilePath)),
            new GalleryMetadataRefresh(_dispatcherQueue, RefreshMetadataFilter),
            _logger,
            "Gallery");
        _metadata.PendingCountChanged += count => PendingMetadataCount = count;

        _sceneCardService.CardAdded += OnCardAdded;
        _sceneCardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ResolutionFilterChanged += OnResolutionFilterChanged;
        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.PluginAnalysisEnabledChanged += OnPluginAnalysisEnabledChanged;
        _settingsViewModel.UseUserRatingsChanged += OnUseUserRatingsChanged;
        _ratingResolver.UserRatings.RatingChanged += OnUserRatingChanged;
    }

    private void OnUseUserRatingsChanged(bool enabled) => _dispatcherQueue.TryEnqueue(() =>
    {
        _ratingResolver.UseUserRatings = enabled;
        ApplyRatings();
    });

    // Raised from a background thread once the rating file is written.
    private void OnUserRatingChanged(string path) => _dispatcherQueue.TryEnqueue(() =>
    {
        if (!_ratingResolver.UseUserRatings || !_cardIndex.TryGetValue(path, out var card)) return;
        var rating = _ratingResolver.Resolve(card);
        if (card.Rating == rating) return;
        card.Rating = rating;
        if (RatingFilter != RatingSet.All) ApplyFilter();
        RatingsChanged?.Invoke();
    });

    private void AssignRating(SceneCard card)
    {
        // The rating folder says it when there is one; a card in the
        // three-level layout has none, and its filed rating says it instead.
        card.FolderRating = SceneRating.FolderOrFiled(
            SceneRating.FromFolders(card.FilePath, _r18FolderNames, _r18GFolderNames),
            FiledRatingOf?.Invoke(card.FilePath));
        card.Rating = _ratingResolver.Resolve(card);
    }

    /// <summary>
    /// The rating a card was filed under (<c>CardState.FiledRating</c>), by
    /// path. Set once the card state service exists; until then, and for a
    /// card with none, the rating folders alone decide.
    /// </summary>
    public Func<string, int?>? FiledRatingOf { get; set; }

    /// <summary>
    /// Re-reads filed ratings: of one card after its state changed, or of all
    /// of them (<paramref name="path"/> null) once the state files are loaded,
    /// which can happen after the cards were. Safe from any thread.
    /// </summary>
    public void RefreshFiledRatings(string? path = null) => _dispatcherQueue.TryEnqueue(() =>
    {
        var changed = false;
        if (path is null)
        {
            foreach (var card in Cards) changed |= Reassign(card);
        }
        else if (_cardIndex.TryGetValue(path, out var card))
        {
            changed = Reassign(card);
        }
        if (changed && RatingFilter != RatingSet.All) ApplyFilter();
        if (changed) RatingsChanged?.Invoke();

        bool Reassign(SceneCard card)
        {
            var before = (card.FolderRating, card.Rating);
            AssignRating(card);
            return before != (card.FolderRating, card.Rating);
        }
    });

    /// <summary>
    /// Reads the platform and user ratings and relabels every card. With a warm
    /// cache this runs twice per load: once for the cached cards as soon as they
    /// are on screen, alongside the scan (so the home page can settle from the
    /// cache), and once after the scan when it found new or changed cards. The
    /// resolver drops an earlier read that finishes after a later one started.
    /// </summary>
    /// <param name="final">The read after the scan: the cards it added stop being pending.</param>
    /// <returns>The posts read, or null when the read was superseded, cancelled or failed.</returns>
    private async Task<int?> ReloadRatingsAsync(IReadOnlyList<string> roots, CancellationToken token, bool final)
    {
        var superseded = false;
        try
        {
            var read = await _ratingResolver.ReloadAsync(Cards.ToArray(), roots, token);
            superseded = read is null;
            if (token.IsCancellationRequested || superseded) return null;
            if (final) _ratingPending.Clear();
            ApplyRatings();
            return read;
        }
        finally
        {
            // Settled even when the read failed (it is logged): the folder and
            // filed ratings are then all there is, and waiting on would keep the
            // home page empty. A cancelled read is followed by another load; a
            // superseded one by the read that superseded it.
            if (!token.IsCancellationRequested && !superseded)
            {
                if (final) _ratingPending.Clear();
                RatingsSettled = true;
                RatingsChanged?.Invoke();
            }
        }
    }

    private void ApplyRatings()
    {
        foreach (var card in Cards)
            card.Rating = _ratingResolver.Resolve(card);
        if (RatingFilter != RatingSet.All)
        {
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        }
        // Random discovery keeps its own rating filter and listens for this.
        RaiseViewRefreshed();
    }

    protected override bool CardPassesFilter(object card) =>
        card is SceneCard sc && BaseFilterPasses(sc);

    private void OnPluginAnalysisEnabledChanged(bool enabled)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _pluginAnalysisEnabled = enabled;
            ShowMetadataFilters = enabled;
            if (enabled)
            {
                StartMetadataScan();
            }
            else
            {
                _metadata.Suspend();
                GameFilter = GameFilterOption.All;
            }
        });
    }

    partial void OnRatingFilterChanged(RatingSet value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnGameFilterChanged(GameFilterOption value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    protected override async Task<int?> CountCardFilesOnDiskAsync()
        => await _sceneCardService.CountCardFilesAsync((await _settingsService.LoadConfigAsync()).FolderPaths);

    protected override int UnreadableCardFileCount => _sceneCardService.LastScanUnreadableCount;

    protected override Task ReloadCardsAsync() => LoadCardsCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task LoadCardsAsync()
        => RunLoadAsync(async cancellationToken =>
        {
            RatingsSettled = false;
            _ratingPending.Clear();
            _cacheShown = false;
            _scanChanged = false;
            // Only the first load of the run is the start; reloads are not timed.
            var timing = StartupTimingRecorder.IsFirstLoad(TimingLibrary) ? new List<StartupPhase>() : null;
            var firstCardsLogged = false;
            void FirstCards()
            {
                if (firstCardsLogged || timing is null || Cards.Count == 0) return;
                firstCardsLogged = true;
                timing.Add(new(TimingLibrary, "first.cards", StartupTimingRecorder.SinceProcessStart, [("cards", Cards.Count)]));
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            long Lap()
            {
                var ms = clock.ElapsedMilliseconds;
                clock.Restart();
                return ms;
            }

            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            _resolutionFilterEnabled = config.ResolutionFilterEnabled;
            _allowedResolutions = [.. config.AllowedResolutions];
            _r18FolderNames = new HashSet<string>([config.R18FolderName], StringComparer.OrdinalIgnoreCase);
            _r18FolderNames.RemoveWhere(string.IsNullOrWhiteSpace);
            _r18GFolderNames = new HashSet<string>([config.R18GFolderName], StringComparer.OrdinalIgnoreCase);
            _r18GFolderNames.RemoveWhere(string.IsNullOrWhiteSpace);
            _ratingResolver.UseUserRatings = config.UseUserRatings;
            ShowFileNames = config.ShowFileNames;
            _pluginAnalysisEnabled = config.PluginAnalysisEnabled;
            ShowMetadataFilters = config.PluginAnalysisEnabled;

            var paths = config.FolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            Lap();
            // The last run's ratings, titles and tags, read alongside the card
            // cache: the cached cards are labelled and filtered from them at
            // once, and the home page need not wait for every sidecar.
            var snapshot = Task.Run(() => _ratingResolver.LoadSnapshot([.. paths]), cancellationToken);
            var cached = await Task.Run(() => _cardCacheService.LoadAll(), cancellationToken);
            var configuredRoots = paths.Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .ToArray();
            var relevantCached = cached.Where(kv =>
                configuredRoots.Any(root => kv.Value.FilePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            timing?.Add(new(TimingLibrary, "cache.read", Lap(), [("cached", cached.Count), ("relevant", relevantCached.Count)]));

            var staleThumbnails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Task<int?>? cachedRatings = null;
            if (relevantCached.Count > 0)
            {
                // One listing per thumbnail folder instead of one File.Exists per
                // card; a thumbnail that is gone is still left off the card, so it
                // is made again when the card is shown.
                var presence = await Task.Run(() => ThumbnailPresence.Find(
                    relevantCached.Select(kv => (kv.Key, kv.Value.ThumbnailPath))), cancellationToken);
                staleThumbnails.UnionWith(presence.Missing);
                timing?.Add(new(TimingLibrary, "thumbs.check", Lap(), [("folders", presence.FoldersListed), ("missing", presence.Missing.Count)]));

                // The cards are made off the UI thread, then put on screen in two
                // steps: a first screenful, and everything else under one deferral.
                // It used to be 200 at a time, and every chunk re-sorted and
                // re-filtered the whole view so far: about 150 refreshes of a
                // growing list, 10-13 s for 30,000 cards (startup_timing.log).
                var made = await Task.Run(() => relevantCached
                    .Select(kv => (kv.Key, Card: FromCache(kv.Value, staleThumbnails)))
                    .ToArray(), cancellationToken);
                var hadSnapshot = await snapshot;
                const int FirstScreen = 200;
                void Add(IEnumerable<(string Key, SceneCard Card)> part)
                {
                    foreach (var (filePath, card) in part)
                    {
                        if (card.ThumbnailPath is not null)
                            _thumbnailPathCache[filePath] = card.ThumbnailPath;
                        AssignRating(card);
                        if (_cardIndex.TryAdd(card.FilePath, card))
                            Cards.Add(card);
                    }
                }
                using (CardsView.DeferRefresh())
                    Add(made.Take(FirstScreen));
                await Task.Yield();
                FirstCards();
                cancellationToken.ThrowIfCancellationRequested();
                if (made.Length > FirstScreen)
                {
                    using (CardsView.DeferRefresh())
                        Add(made.Skip(FirstScreen));
                }
                ApplyFilter();
                RaiseViewRefreshed();
                timing?.Add(new(TimingLibrary, "cache.shown", Lap(), [("cards", Cards.Count)]));

                // The whole cache is on screen: a complete library as of the last
                // run. The home page may use it now, and its ratings are read now,
                // alongside the scan, instead of after it.
                _cacheShown = true;
                // Labelled from the last run's ratings: settled for now. The read
                // below corrects what changed since and raises RatingsChanged again.
                if (hadSnapshot)
                {
                    RatingsSettled = true;
                    RatingsChanged?.Invoke();
                }
                CachedCardsShown?.Invoke();
                cachedRatings = ReloadRatingsAsync([.. paths], cancellationToken, final: false);
                cachedRatings.Observe(_logger, "Gallery.ReloadCachedRatings");
            }

            // A cold cache used to wait for every PNG to be scanned before adding the
            // first card.  Stream modest batches to the UI instead, leaving the grid
            // virtualized while the rest of the folders are still being enumerated.
            // A file the cache describes (same size and write time) is neither
            // opened nor sent here: its card is already on screen. The tracker
            // remembers that the scan saw it, so only cards really gone are removed.
            var seen = new CardScanTracker(relevantCached);
            var addedOrChanged = 0;
            await _sceneCardService.ScanFoldersAsync(paths, batch =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var card in batch)
                    seen.MarkRead(card.FilePath, card.DateCreated);

                PublishScannedBatch(() =>
                {
                    foreach (var fresh in batch)
                    {
                        if (_cardIndex.TryGetValue(fresh.FilePath, out var existing))
                        {
                            // The size too: a file rewritten with its write time kept is a changed file.
                            if (existing.DateModified.Ticks == fresh.DateModified.Ticks && existing.FileSize == fresh.FileSize)
                                continue;

                            _cardIndex.Remove(fresh.FilePath);
                            Cards.Remove(existing);
                        }

                        AssignRating(fresh);
                        if (_cardIndex.TryAdd(fresh.FilePath, fresh))
                        {
                            Cards.Add(fresh);
                            // Its platform rating is only known after the ratings are
                            // read again; until then the home page leaves it out.
                            _ratingPending.Add(fresh.FilePath);
                            _scanChanged = true;
                            addedOrChanged++;
                        }
                    }
                    FirstCards();
                }, cancellationToken);
            }, cancellationToken, skip: file =>
                seen.SkipUnchanged(file.FullName, file.Length, file.LastWriteTime, file.CreationTime));
            var scan = _sceneCardService.LastScanCounts;
            timing?.Add(new(TimingLibrary, "scan", Lap(), [("listed", scan.Listed), ("opened", scan.Opened), ("reused", scan.Reused),
                ("unreadable", _sceneCardService.LastScanUnreadableCount), ("addedOrChanged", addedOrChanged)]));

            // Everything the scan skipped or read is current.  Remove cache entries
            // that were not found without delaying the first visible batch.
            var toRemove = seen.Missing(_cardIndex.Keys).ToList();
            if (toRemove.Count > 0)
            {
                _scanChanged = true;
                using (CardsView.DeferRefresh())
                {
                    foreach (var path in toRemove)
                    {
                        if (_cardIndex.Remove(path, out var old))
                            Cards.Remove(old);
                        _ratingPending.Remove(path);
                    }
                }
            }

            var newCache = new Dictionary<string, CachedCardEntry>(_cardIndex.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (filePath, card) in _cardIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A card kept from a cache written before the creation time was
                // stored has none; the listing just gave it, so take it from there.
                var created = card.DateCreated != default
                    ? card.DateCreated
                    : seen.CreatedOf(filePath) ?? default;
                newCache[filePath] = new CachedCardEntry(
                    card.FilePath, card.FileSize, card.DateModified.Ticks,
                    card.Width, card.Height, card.ThumbnailPath, created.Ticks);
            }
            // Rewritten only when something changed: an unchanged library used to
            // rewrite the whole file on every start.
            var cacheChanged = !CardCacheReuse.SameEntries(cached, newCache);
            if (cacheChanged)
                await Task.Run(() => _cardCacheService.UpdateAll(newCache), cancellationToken);
            timing?.Add(new(TimingLibrary, "cache.write", Lap(), [("written", cacheChanged ? 1 : 0), ("removed", toRemove.Count)]));

            ApplyFilter();
            // The ratings of the cached cards are already being read; read them
            // again only for a cold cache or when the scan brought new cards (their
            // sidecars may live in folders the first read did not visit).
            var finalRatings = cachedRatings is null || addedOrChanged > 0
                ? ReloadRatingsAsync([.. paths], cancellationToken, final: true)
                : cachedRatings;
            finalRatings.Observe(_logger, "Gallery.ReloadRatings");
            _sceneCardService.StartWatching(paths);
            if (_pluginAnalysisEnabled)
                StartMetadataScan();

            if (timing is not null)
            {
                timing.Add(new(TimingLibrary, "load.finished", StartupTimingRecorder.SinceProcessStart, [("cards", Cards.Count)]));
                var ratingsClock = System.Diagnostics.Stopwatch.StartNew();
                _ = finalRatings.ContinueWith(task =>
                {
                    timing.Add(new(TimingLibrary, "ratings.settled", StartupTimingRecorder.SinceProcessStart,
                        [("posts", task.IsCompletedSuccessfully ? task.Result ?? -1 : -1),
                         ("waitedAfterLoad.ms", ratingsClock.ElapsedMilliseconds)]));
                    StartupTimingRecorder.Write(TimingLibrary, timing);
                }, TaskScheduler.Default);
            }
        }, _logger, "Gallery.LoadCanceled", _metadata.CancelScan);

    /// <summary>
    /// Refuses further metadata work and waits for parses already running, so
    /// shutdown can flush the scene metadata cache. Mirrors the character and
    /// coordinate galleries; see App for the shared disposal deadline.
    /// </summary>
    internal Task StopMetadataAsync() => _metadata.StopAsync();

    public void ScanMissingMetadata() => StartMetadataScan();

    public void RescanAllMetadata()
    {
        foreach (var card in Cards)
        {
            _metadataService.Invalidate(card);
            card.MetadataLoaded = false;
        }
        StartMetadataScan();
    }

    private void StartMetadataScan()
        => _metadata.Start(Cards, () => { if (HasMetadataFilter) ApplyFilter(); });

    private static void ApplyMetadata(SceneCard card, SceneMetadata meta)
    {
        card.Game = meta.Game;
        card.MetadataLoaded = true;
    }

    public void RequestThumbnail(
        SceneCard card,
        ThumbnailWorkPriority priority = ThumbnailWorkPriority.Prefetch)
        => RequestThumbnailCore(
            card, priority,
            _thumbnailCacheService.TryGetCachedPath,
            _thumbnailCacheService.EnsureThumbnailAsync,
            _logger, "Gallery",
            (item, path) => _cardCacheService.SetThumbnailPath(item.FilePath, path));

    public void ReleaseThumbnail(SceneCard card)
    {
        ReleaseThumbnailRequest(card.FilePath);
    }


    public SceneCard? GetRandomCard() => GetRandomVisibleCard<SceneCard>();

    private bool BaseFilterPasses(SceneCard card)
        => OriginPasses(card)
           && SceneDiscoveryFilter.Matches(card, _searchKeywords, RatingFilter, GameFilter,
               _resolutionFilterEnabled, _allowedResolutions);

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        var hasRatingFilter = RatingFilter != RatingSet.All;
        var hasSearch = _searchKeywords.Length > 0;
        var filterRes = HasResolutionFilter;
        var hasMetadataFilter = GameFilter != GameFilterOption.All;

        if (!hasRatingFilter && !hasSearch && !filterRes && !hasMetadataFilter && !HasOriginFilter && !HasHiddenFilter)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not SceneCard card) return false;
                return BaseFilterPasses(card);
            };
        }
        RefreshFilterAndNotify();
    }

    private void OnCardAdded(SceneCard card)
    {
        _cardCacheService.Add(new CachedCardEntry(
            card.FilePath, card.FileSize, card.DateModified.Ticks,
            card.Width, card.Height, null, card.DateCreated.Ticks));
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            AssignRating(card);
            Cards.Add(card);
            QueueMetadata(card);
            RaiseViewRefreshed();
        });
    }

    private void QueueMetadata(SceneCard card)
    {
        if (!_pluginAnalysisEnabled) return;
        _metadata.Queue(card, RefreshMetadataFilter);
    }

    private void RefreshMetadataFilter()
    {
        if (!HasMetadataFilter) return;
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnCardRemoved(string path)
    {
        _cardCacheService.Remove(path);
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_cardIndex.Remove(path, out var existing))
            {
                Cards.Remove(existing);
                CardRemovedNotification?.Invoke(path);
            }
        });
    }

    public void Dispose()
    {
        DisposeWorkCancellationSources();
        _metadata.Dispose();
        _sceneCardService.CardAdded -= OnCardAdded;
        _sceneCardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.UseUserRatingsChanged -= OnUseUserRatingsChanged;
        _ratingResolver.UserRatings.RatingChanged -= OnUserRatingChanged;
        _settingsViewModel.ResolutionFilterChanged -= OnResolutionFilterChanged;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        _settingsViewModel.PluginAnalysisEnabledChanged -= OnPluginAnalysisEnabledChanged;
        GC.SuppressFinalize(this);
    }

    public override void Activate()
    {
        base.Activate();
        if (_pluginAnalysisEnabled && Cards.Count > 0)
            StartMetadataScan();
    }

    public override void CancelPendingWork()
    {
        base.CancelPendingWork();
        _metadata.Suspend();
    }
}

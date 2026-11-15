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

/// <summary>Order matches the filter combo boxes, which bind by index.</summary>
public enum RatingFilterOption
{
    All,
    GeneralOnly,
    /// <summary>R-18 including R-18G.</summary>
    R18AndR18G,
    /// <summary>R-18 excluding R-18G.</summary>
    R18Only,
    R18GOnly
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
    private readonly IAppLogger _logger;

    public ObservableCollection<SceneCard> Cards { get; }

    [ObservableProperty]
    public partial RatingFilterOption RatingFilter { get; set; } = RatingFilterOption.All;

    // The user's own ratings exist without any rating-folder provider.
    public bool ShowR18FilterButton => _ratingResolver.UseUserRatings || _pluginService.ImportProviders
        .OfType<IImportDestinationProvider>()
        .Any(p => p.UsesRatingFolders);

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
        OnPropertyChanged(nameof(ShowR18FilterButton));
        ApplyRatings();
    });

    // Raised from a background thread once the rating file is written.
    private void OnUserRatingChanged(string path) => _dispatcherQueue.TryEnqueue(() =>
    {
        if (!_ratingResolver.UseUserRatings || !_cardIndex.TryGetValue(path, out var card)) return;
        var rating = _ratingResolver.Resolve(card);
        if (card.Rating == rating) return;
        card.Rating = rating;
        if (RatingFilter != RatingFilterOption.All) ApplyFilter();
    });

    private void AssignRating(SceneCard card)
    {
        card.FolderRating = SceneRating.FromFolders(card.FilePath, _r18FolderNames, _r18GFolderNames);
        card.Rating = _ratingResolver.Resolve(card);
    }

    /// <summary>
    /// Reads the platform and user ratings after a load and relabels every card.
    /// Runs after the load has finished, so the grid is usable meanwhile with
    /// the folder ratings.
    /// </summary>
    private async Task ReloadRatingsAsync(IReadOnlyList<string> roots, CancellationToken token)
    {
        await _ratingResolver.ReloadAsync(Cards.ToArray(), roots, token);
        if (token.IsCancellationRequested) return;
        ApplyRatings();
    }

    private void ApplyRatings()
    {
        foreach (var card in Cards)
            card.Rating = _ratingResolver.Resolve(card);
        if (RatingFilter != RatingFilterOption.All)
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

    partial void OnRatingFilterChanged(RatingFilterOption value)
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
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            _resolutionFilterEnabled = config.ResolutionFilterEnabled;
            _allowedResolutions = [.. config.AllowedResolutions];
            _r18FolderNames = new HashSet<string>([config.R18FolderName], StringComparer.OrdinalIgnoreCase);
            _r18FolderNames.RemoveWhere(string.IsNullOrWhiteSpace);
            _r18GFolderNames = new HashSet<string>([config.R18GFolderName], StringComparer.OrdinalIgnoreCase);
            _r18GFolderNames.RemoveWhere(string.IsNullOrWhiteSpace);
            _ratingResolver.UseUserRatings = config.UseUserRatings;
            OnPropertyChanged(nameof(ShowR18FilterButton));
            ShowFileNames = config.ShowFileNames;
            _pluginAnalysisEnabled = config.PluginAnalysisEnabled;
            ShowMetadataFilters = config.PluginAnalysisEnabled;

            var paths = config.FolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            var cached = await Task.Run(() => _cardCacheService.LoadAll(), cancellationToken);
            var configuredRoots = paths.Select(p => p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .ToArray();
            var relevantCached = cached.Where(kv =>
                configuredRoots.Any(root => kv.Value.FilePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
            if (relevantCached.Count > 0)
            {
                var staleThumbnails = await Task.Run(() =>
                {
                    var stale = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (path, entry) in relevantCached)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (entry.ThumbnailPath is not null && !File.Exists(entry.ThumbnailPath))
                            stale.Add(path);
                    }
                    return stale;
                }, cancellationToken);

                foreach (var chunk in relevantCached.Chunk(200))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (CardsView.DeferRefresh())
                    {
                        foreach (var (filePath, entry) in chunk)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var card = new SceneCard
                            {
                                FilePath = entry.FilePath,
                                FileSize = entry.FileSize,
                                DateModified = new DateTime(entry.DateModifiedTicks),
                                DateCreated = new DateTime(entry.DateCreatedTicks),
                                Width = entry.Width,
                                Height = entry.Height,
                            };
                            if (entry.ThumbnailPath is not null && !staleThumbnails.Contains(filePath))
                            {
                                card.ThumbnailPath = entry.ThumbnailPath;
                                _thumbnailPathCache[filePath] = entry.ThumbnailPath;
                            }
                            AssignRating(card);
                            if (_cardIndex.TryAdd(card.FilePath, card))
                                Cards.Add(card);
                        }
                    }
                    await Task.Yield();
                }
                ApplyFilter();
                RaiseViewRefreshed();
            }

            // A cold cache used to wait for every PNG to be scanned before adding the
            // first card.  Stream modest batches to the UI instead, leaving the grid
            // virtualized while the rest of the folders are still being enumerated.
            var scannedIndex = new ConcurrentDictionary<string, SceneCard>(StringComparer.OrdinalIgnoreCase);
            await _sceneCardService.ScanFoldersAsync(paths, batch =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var card in batch)
                    scannedIndex.TryAdd(card.FilePath, card);

                PublishScannedBatch(() =>
                {
                    foreach (var fresh in batch)
                    {
                        if (_cardIndex.TryGetValue(fresh.FilePath, out var existing))
                        {
                            if (existing.DateModified.Ticks == fresh.DateModified.Ticks)
                                continue;

                            _cardIndex.Remove(fresh.FilePath);
                            Cards.Remove(existing);
                        }

                        AssignRating(fresh);
                        if (_cardIndex.TryAdd(fresh.FilePath, fresh))
                            Cards.Add(fresh);
                    }
                }, cancellationToken);
            }, cancellationToken);

            // Everything received in the batches is current.  Remove cache entries
            // that were not found without delaying the first visible batch.
            var toRemove = _cardIndex.Keys
                .Where(filePath => !scannedIndex.ContainsKey(filePath))
                .ToList();
            if (toRemove.Count > 0)
            {
                using (CardsView.DeferRefresh())
                {
                    foreach (var path in toRemove)
                    {
                        if (_cardIndex.Remove(path, out var old))
                            Cards.Remove(old);
                    }
                }
            }

            var newCache = new Dictionary<string, CachedCardEntry>(_cardIndex.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (filePath, card) in _cardIndex)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A card kept from a cache written before the creation time was
                // stored has none; the scan just read it, so take it from there.
                var created = card.DateCreated != default
                    ? card.DateCreated
                    : scannedIndex.TryGetValue(filePath, out var scanned) ? scanned.DateCreated : default;
                newCache[filePath] = new CachedCardEntry(
                    card.FilePath, card.FileSize, card.DateModified.Ticks,
                    card.Width, card.Height, card.ThumbnailPath, created.Ticks);
            }
            await Task.Run(() => _cardCacheService.UpdateAll(newCache), cancellationToken);

            ApplyFilter();
            ReloadRatingsAsync([.. paths], cancellationToken).Observe(_logger, "Gallery.ReloadRatings");
            _sceneCardService.StartWatching(paths);
            if (_pluginAnalysisEnabled)
                StartMetadataScan();
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

        var hasRatingFilter = RatingFilter != RatingFilterOption.All;
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

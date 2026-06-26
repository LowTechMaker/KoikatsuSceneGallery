using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public enum SortOption
{
    Name,
    DateModified,
    FileSize,
    Shuffle
}

public static class ShuffleConstants
{
    public const int PoolSize = 20;
}

public enum GameFilterOption
{
    All,
    Koikatsu,
    KoikatsuSunshine,
    Unknown
}

public partial class GalleryViewModel : ObservableObject, IDisposable
{
    private readonly SceneCardService _sceneCardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly SceneMetadataService _metadataService;
    private readonly SceneCardCacheService _cardCacheService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<SceneCard> Cards { get; } = [];
    public AdvancedCollectionView CardsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShuffleMode))]
    public partial SortOption SelectedSort { get; set; } = SortOption.Name;

    [ObservableProperty]
    public partial bool SortAscending { get; set; } = true;

    public bool IsShuffleMode => SelectedSort == SortOption.Shuffle;

    private int _shuffleDisplayCount;
    private readonly List<object> _shuffleQueue = [];
    private readonly Dictionary<object, int> _shuffleOrderMap = [];
    private readonly HashSet<object> _shuffleUsedCards = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && CardsView.Count == 0;

    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowR18Content { get; set; } = true;

    public bool ShowR18FilterButton => App.PluginService.ImportProviders
        .OfType<IImportDestinationProvider>()
        .Any(p => p.UsesRatingFolders);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneratingThumbnails))]
    public partial int PendingThumbnailCount { get; set; }

    public bool IsGeneratingThumbnails => PendingThumbnailCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    [NotifyPropertyChangedFor(nameof(IsNotParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;
    public bool IsNotParsingMetadata => PendingMetadataCount == 0;

    /// <summary>Whether the plugin-metadata filters are shown (mirrors the setting; off by default).</summary>
    [ObservableProperty]
    public partial bool ShowMetadataFilters { get; set; }

    [ObservableProperty]
    public partial GameFilterOption GameFilter { get; set; } = GameFilterOption.All;

    private bool HasMetadataFilter =>
        GameFilter != GameFilterOption.All;

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];
    private HashSet<string> _r18FolderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbnailCts;
    private readonly SemaphoreSlim _thumbnailGate = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _thumbnailPathCache = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _metadataCts;
    // Metadata parsing is I/O-bound (reading file bytes), not CPU-bound like
    // thumbnail generation, so it uses a small fixed concurrency instead of
    // scaling with the core count. This keeps the first full scan gentle on
    // spinning disks (avoids seek thrashing from many concurrent reads) while
    // being more than enough throughput on an SSD, where the per-file work is tiny.
    private const int MetadataParseConcurrency = 4;
    private readonly SemaphoreSlim _metadataGate = new(MetadataParseConcurrency);
    private DispatcherQueueTimer? _metadataRefreshTimer;
    private bool _pluginAnalysisEnabled;

    public GalleryViewModel(SceneCardService sceneCardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, SceneMetadataService metadataService, SceneCardCacheService cardCacheService)
    {
        _sceneCardService = sceneCardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _cardCacheService = cardCacheService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        CardsView = new AdvancedCollectionView(Cards, true);
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        ApplySort();

        _sceneCardService.CardAdded += OnCardAdded;
        _sceneCardService.CardRemoved += OnCardRemoved;

        App.SettingsViewModel.ResolutionFilterChanged += OnResolutionFilterChanged;
        App.SettingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        App.SettingsViewModel.PluginAnalysisEnabledChanged += OnPluginAnalysisEnabledChanged;
    }

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
                // Stop any in-flight scan and clear metadata filters so the
                // gallery isn't left filtered by tags the user just turned off.
                _metadataCts?.Cancel();
                _metadataRefreshTimer?.Stop();
                PendingMetadataCount = 0;
                GameFilter = GameFilterOption.All;
            }
        });
    }

    partial void OnSearchTextChanged(string value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnShowR18ContentChanged(bool value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnGameFilterChanged(GameFilterOption value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (value == SortOption.Shuffle)
            BuildShuffleQueue();
        else
            ClearShuffleState();
        ApplySort();
        ApplyFilter();
    }

    partial void OnSortAscendingChanged(bool value)
    {
        ApplySort();
    }

    [RelayCommand]
    private async Task LoadCardsAsync()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _thumbnailCts = new CancellationTokenSource();
        _thumbnailRequested.Clear();
        _thumbnailPathCache.Clear();
        PendingThumbnailCount = 0;

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            _resolutionFilterEnabled = config.ResolutionFilterEnabled;
            _allowedResolutions = [.. config.AllowedResolutions];
            _r18FolderNames = new HashSet<string>(
                [config.R18FolderName, config.R18GFolderName],
                StringComparer.OrdinalIgnoreCase);
            _r18FolderNames.RemoveWhere(string.IsNullOrWhiteSpace);
            ShowFileNames = config.ShowFileNames;
            _pluginAnalysisEnabled = config.PluginAnalysisEnabled;
            ShowMetadataFilters = config.PluginAnalysisEnabled;

            var paths = config.FolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            // Phase 1: instant load from persisted card cache
            var cached = await Task.Run(() => _cardCacheService.LoadAll());
            // Filter to cards whose parent folder is still in the configured paths,
            // so cards from removed folders don't flash on screen before the diff.
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
                        if (entry.ThumbnailPath is not null && !File.Exists(entry.ThumbnailPath))
                            stale.Add(path);
                    }
                    return stale;
                });

                using (CardsView.DeferRefresh())
                {
                    foreach (var (filePath, entry) in relevantCached)
                    {
                        var card = new SceneCard
                        {
                            FilePath = entry.FilePath,
                            FileSize = entry.FileSize,
                            DateModified = new DateTime(entry.DateModifiedTicks),
                            Width = entry.Width,
                            Height = entry.Height,
                        };
                        if (entry.ThumbnailPath is not null && !staleThumbnails.Contains(filePath))
                        {
                            card.ThumbnailPath = entry.ThumbnailPath;
                            _thumbnailPathCache[filePath] = entry.ThumbnailPath;
                        }
                        card.IsR18Content = IsR18Path(card.FilePath);
                        if (_cardIndex.TryAdd(card.FilePath, card))
                            Cards.Add(card);
                    }
                }
                ApplyFilter();
                ViewRefreshed?.Invoke();
            }

            // Phase 2: background filesystem diff
            var scanned = await _sceneCardService.ScanFoldersAsync(paths);
            var scannedIndex = new Dictionary<string, SceneCard>(scanned.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var card in scanned)
                scannedIndex.TryAdd(card.FilePath, card);

            // Detect removed and modified cards
            var toRemove = new List<string>();
            var toUpdate = new List<SceneCard>();
            foreach (var (filePath, existing) in _cardIndex)
            {
                if (!scannedIndex.TryGetValue(filePath, out var fresh))
                {
                    toRemove.Add(filePath);
                }
                else if (existing.DateModified.Ticks != fresh.DateModified.Ticks)
                {
                    toRemove.Add(filePath);
                    toUpdate.Add(fresh);
                }
            }

            // Detect added cards
            var toAdd = new List<SceneCard>();
            foreach (var (filePath, fresh) in scannedIndex)
            {
                if (!_cardIndex.ContainsKey(filePath))
                    toAdd.Add(fresh);
            }

            if (toRemove.Count > 0 || toAdd.Count > 0 || toUpdate.Count > 0)
            {
                using (CardsView.DeferRefresh())
                {
                    foreach (var path in toRemove)
                    {
                        if (_cardIndex.Remove(path, out var old))
                            Cards.Remove(old);
                    }
                    foreach (var card in toUpdate)
                    {
                        card.IsR18Content = IsR18Path(card.FilePath);
                        if (_cardIndex.TryAdd(card.FilePath, card))
                            Cards.Add(card);
                    }
                    foreach (var card in toAdd)
                    {
                        card.IsR18Content = IsR18Path(card.FilePath);
                        if (_cardIndex.TryAdd(card.FilePath, card))
                            Cards.Add(card);
                    }
                }
            }

            // Persist the reconciled state
            var newCache = new Dictionary<string, CachedCardEntry>(_cardIndex.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (filePath, card) in _cardIndex)
            {
                newCache[filePath] = new CachedCardEntry(
                    card.FilePath, card.FileSize, card.DateModified.Ticks,
                    card.Width, card.Height, card.ThumbnailPath);
            }
            await Task.Run(() => _cardCacheService.UpdateAll(newCache));

            ApplyFilter();
            _sceneCardService.StartWatching(paths);
            if (_pluginAnalysisEnabled)
                StartMetadataScan();
        }
        finally
        {
            IsLoading = false;
            CardsReloaded?.Invoke();
        }
    }

    /// <summary>
    /// Manually parses metadata for cards not yet recorded in the cache.
    /// Cards already classified (cache hits) are left untouched.
    /// </summary>
    public void ScanMissingMetadata() => StartMetadataScan();

    /// <summary>
    /// Manually re-parses every loaded card from scratch, discarding any cached
    /// classification first (e.g. to pick up changed rules or fix bad data).
    /// </summary>
    public void RescanAllMetadata()
    {
        foreach (var card in Cards)
        {
            _metadataService.Invalidate(card);
            card.MetadataLoaded = false;
        }
        StartMetadataScan();
    }

    /// <summary>
    /// Parses plugin metadata for every card that doesn't have it yet. Cache
    /// hits are applied immediately; misses are parsed on background threads
    /// with bounded concurrency (same gating as thumbnails). While a metadata
    /// filter is active the view is refreshed on a throttled timer so newly
    /// classified cards fold in without an O(n²) refresh storm.
    /// </summary>
    private void StartMetadataScan()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = new CancellationTokenSource();
        var token = _metadataCts.Token;
        PendingMetadataCount = 0;

        var pending = new List<SceneCard>();
        foreach (var card in Cards)
        {
            if (card.MetadataLoaded) continue;
            if (_metadataService.TryGetCached(card, out var meta))
                ApplyMetadata(card, meta);
            else
                pending.Add(card);
        }

        if (pending.Count == 0)
        {
            if (HasMetadataFilter) ApplyFilter();
            return;
        }

        PendingMetadataCount = pending.Count;
        StartMetadataRefreshTimer();
        foreach (var card in pending)
            _ = Task.Run(() => ParseMetadataAsync(card, token), token);
    }

    private async Task ParseMetadataAsync(SceneCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _metadataGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var meta = _metadataService.ParseAndCache(card);
                if (!cancellationToken.IsCancellationRequested)
                    _dispatcherQueue.TryEnqueue(() => ApplyMetadata(card, meta));
            }
            finally
            {
                _metadataGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                if (PendingMetadataCount > 0) PendingMetadataCount--;
                if (PendingMetadataCount == 0) OnMetadataScanCompleted();
            });
        }
    }

    private static void ApplyMetadata(SceneCard card, SceneMetadata meta)
    {
        card.Game = meta.Game;
        card.MetadataLoaded = true;
    }

    private void StartMetadataRefreshTimer()
    {
        _metadataRefreshTimer ??= _dispatcherQueue.CreateTimer();
        _metadataRefreshTimer.Interval = TimeSpan.FromMilliseconds(750);
        _metadataRefreshTimer.IsRepeating = true;
        _metadataRefreshTimer.Tick -= OnMetadataRefreshTick;
        _metadataRefreshTimer.Tick += OnMetadataRefreshTick;
        _metadataRefreshTimer.Start();
    }

    private void OnMetadataRefreshTick(DispatcherQueueTimer sender, object args)
    {
        if (HasMetadataFilter)
        {
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void OnMetadataScanCompleted()
    {
        _metadataRefreshTimer?.Stop();
        if (HasMetadataFilter)
        {
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Requests a thumbnail for a single card on demand (called as cards scroll
    /// into view). Cached thumbnails resolve almost instantly; uncached ones are
    /// generated with limited concurrency so the UI stays responsive even for a
    /// very large library.
    /// </summary>
    public void RequestThumbnail(SceneCard card)
    {
        if (card.HasThumbnail) return;
        if (_thumbnailPathCache.TryGetValue(card.FilePath, out var cached))
        {
            card.ThumbnailPath = cached;
            return;
        }

        // Fast-path: resolve from disk cache without queuing async work
        var diskCached = _thumbnailCacheService.TryGetCachedPath(card);
        if (diskCached is not null)
        {
            _thumbnailPathCache[card.FilePath] = diskCached;
            card.ThumbnailPath = diskCached;
            return;
        }

        if (!_thumbnailRequested.Add(card.FilePath)) return;

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        PendingThumbnailCount++;
        _ = Task.Run(() => GenerateOneAsync(card, token));
    }

    // Eviction-on-recycle was removed: clearing ThumbnailPath when a container
    // recycled raced with re-prepare during scroll/relayout and left visible
    // cards blank or stale. Thumbnails now stay loaded once generated. Memory is
    // bounded in practice by how many cards are ever scrolled into view, and each
    // is a small 300px-wide decoded image (DecodePixelWidth).
    public void ReleaseThumbnail(SceneCard card)
    {
    }

    private async Task GenerateOneAsync(SceneCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (card.HasThumbnail) return;
                var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card).ConfigureAwait(false);
                if (thumbnailPath != null && !cancellationToken.IsCancellationRequested)
                {
                    _cardCacheService.SetThumbnailPath(card.FilePath, thumbnailPath);
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        _thumbnailPathCache[card.FilePath] = thumbnailPath;
                        card.ThumbnailPath = thumbnailPath;
                    });
                }
            }
            finally
            {
                _thumbnailGate.Release();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            // Skip the decrement for a superseded load; that load reset the
            // counter to 0, so decrementing here would drive it negative.
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                PendingThumbnailCount--;
                // If generation produced nothing (null result or error), drop the
                // requested-mark so a later request can retry instead of leaving
                // the card permanently blank.
                if (!card.HasThumbnail)
                    _thumbnailRequested.Remove(card.FilePath);
            });
        }
    }

    private void OnResolutionFilterChanged(bool enabled, HashSet<string> resolutions)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _resolutionFilterEnabled = enabled;
            _allowedResolutions = resolutions;
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        });
    }

    private void OnShowFileNamesSettingChanged(bool value)
    {
        _dispatcherQueue.TryEnqueue(() => ShowFileNames = value);
    }

    public void SetShuffleDisplayCount(int count)
    {
        if (_shuffleDisplayCount == count) return;
        _shuffleDisplayCount = count;
        if (IsShuffleMode) ApplyFilter();
    }

    public void Reshuffle()
    {
        AdvanceShuffleQueue();
        ApplySort();
        ApplyFilter();
    }

    private void ApplySort()
    {
        using (CardsView.DeferRefresh())
        {
            CardsView.SortDescriptions.Clear();
            if (SelectedSort == SortOption.Shuffle)
            {
                CardsView.SortDescriptions.Add(
                    new SortDescription(SortDirection.Ascending, new ShuffleQueueComparer(_shuffleOrderMap)));
                return;
            }
            var direction = SortAscending ? SortDirection.Ascending : SortDirection.Descending;
            string propertyName = SelectedSort switch
            {
                SortOption.Name => nameof(SceneCard.FileName),
                SortOption.DateModified => nameof(SceneCard.DateModified),
                SortOption.FileSize => nameof(SceneCard.FileSize),
                _ => nameof(SceneCard.FileName)
            };

            CardsView.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }
    }

    private bool BaseFilterPasses(SceneCard card)
    {
        if (!ShowR18Content && card.IsR18Content) return false;

        var keywords = SearchText.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0);
        foreach (var kw in keywords)
            if (!card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return false;

        if (_resolutionFilterEnabled && _allowedResolutions.Count > 0
            && !_allowedResolutions.Contains(card.Resolution))
            return false;

        if (GameFilter != GameFilterOption.All)
        {
            if (!card.MetadataLoaded) return false;
            var targetGame = GameFilter switch
            {
                GameFilterOption.Koikatsu => GameVersion.Koikatsu,
                GameFilterOption.KoikatsuSunshine => GameVersion.KoikatsuSunshine,
                _ => GameVersion.Unknown
            };
            if (card.Game != targetGame) return false;
        }

        return true;
    }

    private void BuildShuffleQueue()
    {
        var candidates = new List<SceneCard>();
        foreach (var card in Cards)
            if (BaseFilterPasses(card))
                candidates.Add(card);

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int poolSize = Math.Min(ShuffleConstants.PoolSize, candidates.Count);
        _shuffleQueue.Clear();
        _shuffleUsedCards.Clear();
        _shuffleOrderMap.Clear();

        for (int i = 0; i < poolSize; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleOrderMap[candidates[i]] = i;
            _shuffleUsedCards.Add(candidates[i]);
        }
    }

    private void AdvanceShuffleQueue()
    {
        int displayCount = Math.Min(_shuffleDisplayCount, _shuffleQueue.Count);
        if (displayCount <= 0 || _shuffleQueue.Count == 0) return;

        var tail = _shuffleQueue.Skip(displayCount).ToList();

        var candidates = new List<SceneCard>();
        foreach (var card in Cards)
            if (BaseFilterPasses(card) && !_shuffleUsedCards.Contains(card))
                candidates.Add(card);

        if (candidates.Count == 0)
        {
            _shuffleUsedCards.Clear();
            foreach (var item in tail)
                _shuffleUsedCards.Add(item);
            foreach (var card in Cards)
                if (BaseFilterPasses(card) && !_shuffleUsedCards.Contains(card))
                    candidates.Add(card);
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int needed = ShuffleConstants.PoolSize - tail.Count;
        int take = Math.Min(needed, candidates.Count);

        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleQueue.AddRange(tail);
        for (int i = 0; i < take; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleUsedCards.Add(candidates[i]);
        }

        for (int i = 0; i < _shuffleQueue.Count; i++)
            _shuffleOrderMap[_shuffleQueue[i]] = i;
    }

    private void ClearShuffleState()
    {
        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleUsedCards.Clear();
    }

    public SceneCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as SceneCard;
    }

    private void ApplyFilter()
    {
        var isShuffleMode = IsShuffleMode;

        if (isShuffleMode)
        {
            int displayCount = Math.Min(_shuffleDisplayCount, _shuffleQueue.Count);
            var displaySet = new HashSet<object>();
            for (int i = 0; i < displayCount; i++)
                displaySet.Add(_shuffleQueue[i]);

            CardsView.Filter = item => displaySet.Contains(item);
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
            ViewRefreshed?.Invoke();
            return;
        }

        var showR18Content = ShowR18Content;
        var keywords = SearchText.Split(',').Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
        var hasSearch = keywords.Length > 0;
        var filterRes = _resolutionFilterEnabled && _allowedResolutions.Count > 0;
        var hasMetadataFilter = GameFilter != GameFilterOption.All;

        if (showR18Content && !hasSearch && !filterRes && !hasMetadataFilter)
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
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
        ViewRefreshed?.Invoke();
    }

    private void OnCardAdded(SceneCard card)
    {
        _cardCacheService.Add(new CachedCardEntry(
            card.FilePath, card.FileSize, card.DateModified.Ticks,
            card.Width, card.Height, null));
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            card.IsR18Content = IsR18Path(card.FilePath);
            Cards.Add(card);
            QueueMetadata(card);
            ViewRefreshed?.Invoke();
        });
    }

    private void QueueMetadata(SceneCard card)
    {
        if (!_pluginAnalysisEnabled) return;
        if (card.MetadataLoaded) return;
        if (_metadataService.TryGetCached(card, out var meta))
        {
            ApplyMetadata(card, meta);
            if (HasMetadataFilter)
            {
                CardsView.RefreshFilter();
                OnPropertyChanged(nameof(IsEmpty));
            }
            return;
        }

        _metadataCts ??= new CancellationTokenSource();
        var token = _metadataCts.Token;
        PendingMetadataCount++;
        _ = Task.Run(() => ParseMetadataAsync(card, token), token);
    }

    public event Action<string>? CardRemovedNotification;
    public event Action? CardsReloaded;
    public event Action? ViewRefreshed;

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

    private bool IsR18Path(string filePath)
    {
        if (_r18FolderNames.Count == 0) return false;

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory)) return false;

        foreach (var segment in directory.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (_r18FolderNames.Contains(segment))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataRefreshTimer?.Stop();
        _sceneCardService.CardAdded -= OnCardAdded;
        _sceneCardService.CardRemoved -= OnCardRemoved;
        App.SettingsViewModel.ResolutionFilterChanged -= OnResolutionFilterChanged;
        App.SettingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        App.SettingsViewModel.PluginAnalysisEnabledChanged -= OnPluginAnalysisEnabledChanged;
        GC.SuppressFinalize(this);
    }
}

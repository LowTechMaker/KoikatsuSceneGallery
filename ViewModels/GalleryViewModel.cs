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
    private readonly IAppLogger _logger;

    public ObservableCollection<SceneCard> Cards { get; }

    [ObservableProperty]
    public partial bool ShowR18Content { get; set; } = true;

    public bool ShowR18FilterButton => _pluginService.ImportProviders
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

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];
    private HashSet<string> _r18FolderNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SceneCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _metadataCts;
    private const int MetadataParseConcurrency = 4;
    private readonly SemaphoreSlim _metadataGate = new(MetadataParseConcurrency);
    private DispatcherQueueTimer? _metadataRefreshTimer;
    private bool _pluginAnalysisEnabled;

    public event Action<string>? CardRemovedNotification;

    public GalleryViewModel(SceneCardService sceneCardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, SceneMetadataService metadataService, SceneCardCacheService cardCacheService, SettingsViewModel settingsViewModel, PluginService pluginService, IAppLogger logger)
        : base(new ObservableCollection<SceneCard>())
    {
        Cards = (ObservableCollection<SceneCard>)_cardsSource;
        _sceneCardService = sceneCardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _cardCacheService = cardCacheService;
        _settingsViewModel = settingsViewModel;
        _pluginService = pluginService;
        _logger = logger;

        _sceneCardService.CardAdded += OnCardAdded;
        _sceneCardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ResolutionFilterChanged += OnResolutionFilterChanged;
        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.PluginAnalysisEnabledChanged += OnPluginAnalysisEnabledChanged;
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
                _metadataCts?.Cancel();
                _metadataRefreshTimer?.Stop();
                PendingMetadataCount = 0;
                GameFilter = GameFilterOption.All;
            }
        });
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

    [RelayCommand]
    private async Task LoadCardsAsync()
    {
        ResetThumbnailState();

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

            var cached = await Task.Run(() => _cardCacheService.LoadAll());
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
                RaiseViewRefreshed();
            }

            var scanned = await _sceneCardService.ScanFoldersAsync(paths);
            var scannedIndex = new Dictionary<string, SceneCard>(scanned.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var card in scanned)
                scannedIndex.TryAdd(card.FilePath, card);

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
            RaiseCardsReloaded();
        }
    }

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
            Task.Run(() => ParseMetadataAsync(card, token), token)
                .Observe(_logger, "Gallery.ParseMetadata");
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
        catch (OperationCanceledException ex) { _logger.LogError("Gallery.ParseMetadataCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("Gallery.ParseMetadata", ex, card.FilePath); }
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

    public void RequestThumbnail(SceneCard card)
    {
        if (card.HasThumbnail) return;
        if (_thumbnailPathCache.TryGetValue(card.FilePath, out var cached))
        {
            card.ThumbnailPath = cached;
            return;
        }

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
        Task.Run(() => GenerateOneAsync(card, token))
            .Observe(_logger, "Gallery.GenerateThumbnail");
    }

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
        catch (OperationCanceledException ex) { _logger.LogError("Gallery.GenerateThumbnailCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("Gallery.GenerateThumbnail", ex, card.FilePath); }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                PendingThumbnailCount--;
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

    public SceneCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as SceneCard;
    }

    private bool BaseFilterPasses(SceneCard card)
    {
        if (!ShowR18Content && card.IsR18Content) return false;

        foreach (var kw in _searchKeywords)
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

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        var showR18Content = ShowR18Content;
        var hasSearch = _searchKeywords.Length > 0;
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
        RefreshFilterAndNotify();
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
            RaiseViewRefreshed();
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
        Task.Run(() => ParseMetadataAsync(card, token), token)
            .Observe(_logger, "Gallery.ParseAddedCardMetadata");
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
        _settingsViewModel.ResolutionFilterChanged -= OnResolutionFilterChanged;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        _settingsViewModel.PluginAnalysisEnabledChanged -= OnPluginAnalysisEnabledChanged;
        GC.SuppressFinalize(this);
    }
}

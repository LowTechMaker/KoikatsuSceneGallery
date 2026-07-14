using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public partial class CoordinateGalleryViewModel : GalleryViewModelBase, IDisposable
{
    private readonly CoordinateCardService _cardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly CoordinateMetadataService _metadataService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IAppLogger _logger;

    public ObservableCollection<CoordinateCard> Cards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;

    private bool HasResolutionFilter => _resolutionFilterEnabled && _allowedResolutions.Count > 0;

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];

    private readonly Dictionary<string, CoordinateCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _metadataCts;
    private const int MetadataParseConcurrency = 4;
    private DispatcherQueueTimer? _metadataRefreshTimer;

    public CoordinateGalleryViewModel(CoordinateCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, CoordinateMetadataService metadataService, SettingsViewModel settingsViewModel, IAppLogger logger)
        : base(new ObservableCollection<CoordinateCard>())
    {
        Cards = (ObservableCollection<CoordinateCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _settingsViewModel = settingsViewModel;
        _logger = logger;

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.CoordinateResolutionFilterChanged += OnCoordinateResolutionFilterChanged;
    }

    protected override bool CardPassesFilter(object card) =>
        card is CoordinateCard cc && BaseFilterPasses(cc);

    [RelayCommand]
    private async Task LoadCardsAsync()
    {
        var cancellationToken = BeginLoad();
        _metadataCts?.Cancel();
        ResetThumbnailState();

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ShowFileNames = config.ShowFileNames;
            _resolutionFilterEnabled = config.CoordinateResolutionFilterEnabled;
            _allowedResolutions = [.. config.CoordinateAllowedResolutions];

            var paths = config.CoordinateFolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            await _cardService.ScanFoldersAsync(paths, batch =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var processed = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        processed.TrySetResult();
                        return;
                    }
                    using (CardsView.DeferRefresh())
                    {
                        foreach (var card in batch)
                        {
                            if (!_cardIndex.TryAdd(card.FilePath, card)) continue;
                            Cards.Add(card);
                        }
                    }
                    processed.TrySetResult();
                });
                processed.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken)
                    .GetAwaiter().GetResult();
            }, cancellationToken);

            ApplyFilter();
            _cardService.StartWatching(paths);
            StartMetadataScan();
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("CoordinateGallery.LoadCanceled", ex);
        }
        finally
        {
            if (_loadCts?.Token == cancellationToken)
                IsLoading = false;
            RaiseCardsReloaded();
        }
    }

    private void StartMetadataScan()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = new CancellationTokenSource();
        var token = _metadataCts.Token;
        PendingMetadataCount = 0;

        var pending = new List<CoordinateCard>();
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
            ApplyFilter();
            return;
        }

        PendingMetadataCount = pending.Count;
        StartMetadataRefreshTimer();
        BoundedAsyncPipeline.ForEachAsync(
                pending,
                MetadataParseConcurrency,
                (card, cancellationToken) => new ValueTask(ParseMetadataAsync(card, cancellationToken)),
                token)
            .Observe(_logger, "CoordinateGallery.ParseMetadata");
    }

    private async Task ParseMetadataAsync(CoordinateCard card, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var meta = _metadataService.ParseAndCache(card, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                _dispatcherQueue.TryEnqueue(() => ApplyMetadata(card, meta));
        }
        catch (OperationCanceledException ex) { _logger.LogError("CoordinateGallery.ParseMetadataCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("CoordinateGallery.ParseMetadata", ex, card.FilePath); }
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

    private static void ApplyMetadata(CoordinateCard card, CoordinateMetadata meta)
    {
        card.CoordinateName = meta.CoordinateName ?? string.Empty;
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
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnMetadataScanCompleted()
    {
        _metadataRefreshTimer?.Stop();
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void RequestThumbnail(CoordinateCard card)
    {
        if (card.HasThumbnail) return;
        if (_thumbnailPathCache.TryGetValue(card.FilePath, out var cached))
        {
            card.ThumbnailPath = cached;
            return;
        }

        var diskCached = _thumbnailCacheService.TryGetCachedPath(card.FilePath, card.DateModified);
        if (diskCached is not null)
        {
            _thumbnailPathCache[card.FilePath] = diskCached;
            card.ThumbnailPath = diskCached;
            return;
        }

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        if (token.IsCancellationRequested || !_thumbnailRequested.Add(card.FilePath)) return;

        PendingThumbnailCount++;
        Task.Run(() => GenerateOneAsync(card, token), token)
            .Observe(_logger, "CoordinateGallery.GenerateThumbnail");
    }

    public void ReleaseThumbnail(CoordinateCard card)
    {
    }

    private async Task GenerateOneAsync(CoordinateCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (card.HasThumbnail) return;
                var thumbnailPath = await _thumbnailCacheService
                    .EnsureThumbnailAsync(card.FilePath, card.DateModified, cancellationToken)
                    .ConfigureAwait(false);
                if (thumbnailPath != null && !cancellationToken.IsCancellationRequested)
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        _thumbnailPathCache[card.FilePath] = thumbnailPath;
                        card.ThumbnailPath = thumbnailPath;
                    });
            }
            finally
            {
                _thumbnailGate.Release();
            }
        }
        catch (OperationCanceledException ex) { _logger.LogError("CoordinateGallery.GenerateThumbnailCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("CoordinateGallery.GenerateThumbnail", ex, card.FilePath); }
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

    private void OnCoordinateResolutionFilterChanged(bool enabled, HashSet<string> resolutions)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _resolutionFilterEnabled = enabled;
            _allowedResolutions = resolutions;
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        });
    }

    public CoordinateCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as CoordinateCard;
    }

    private bool BaseFilterPasses(CoordinateCard card)
    {
        foreach (var kw in _searchKeywords)
        {
            bool inPath = card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase);
            bool inName = card.MetadataLoaded
                && card.CoordinateName.Contains(kw, StringComparison.OrdinalIgnoreCase);
            if (!inPath && !inName) return false;
        }

        if (_resolutionFilterEnabled && _allowedResolutions.Count > 0
            && !_allowedResolutions.Contains(card.Resolution))
            return false;

        return true;
    }

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        var hasSearch = _searchKeywords.Length > 0;
        var filterRes = _resolutionFilterEnabled && _allowedResolutions.Count > 0;

        if (!hasSearch && !filterRes)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not CoordinateCard card) return false;
                return BaseFilterPasses(card);
            };
        }
        RefreshFilterAndNotify();
    }

    private void OnCardAdded(CoordinateCard card)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            Cards.Add(card);
            RequestThumbnail(card);
            QueueMetadata(card);
        });
    }

    private void QueueMetadata(CoordinateCard card)
    {
        if (card.MetadataLoaded) return;
        if (_metadataService.TryGetCached(card, out var meta))
        {
            ApplyMetadata(card, meta);
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
            return;
        }

        _metadataCts ??= new CancellationTokenSource();
        var token = _metadataCts.Token;
        if (token.IsCancellationRequested) return;
        PendingMetadataCount++;
        BoundedAsyncPipeline.ForEachAsync(
                [card],
                MetadataParseConcurrency,
                (item, cancellationToken) => new ValueTask(ParseMetadataAsync(item, cancellationToken)),
                token)
            .Observe(_logger, "CoordinateGallery.ParseAddedCardMetadata");
    }

    private void OnCardRemoved(string path)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_cardIndex.Remove(path, out var existing))
                Cards.Remove(existing);
        });
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataRefreshTimer?.Stop();
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        _settingsViewModel.CoordinateResolutionFilterChanged -= OnCoordinateResolutionFilterChanged;
        GC.SuppressFinalize(this);
    }

    public override void Activate()
    {
        base.Activate();
        if (Cards.Count > 0)
            StartMetadataScan();
    }

    public override void CancelPendingWork()
    {
        base.CancelPendingWork();
        _metadataCts?.Cancel();
        _metadataRefreshTimer?.Stop();
        PendingMetadataCount = 0;
    }
}

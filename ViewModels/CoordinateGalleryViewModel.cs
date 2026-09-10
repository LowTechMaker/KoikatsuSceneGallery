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
    public MetadataFilterState MetadataFilters { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;

    private bool HasResolutionFilter => _resolutionFilterEnabled && _allowedResolutions.Count > 0;

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];

    private readonly Dictionary<string, CoordinateCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    private readonly MetadataScanCoordinator<CoordinateCard, CoordinateMetadata> _metadataScan;
    private readonly GalleryMetadataRefresh _metadataRefresh;

    public CoordinateGalleryViewModel(CoordinateCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, CoordinateMetadataService metadataService, SettingsViewModel settingsViewModel, ThumbnailPriorityScheduler thumbnailScheduler, IAppLogger logger)
        : base(new ObservableCollection<CoordinateCard>(), thumbnailScheduler)
    {
        Cards = (ObservableCollection<CoordinateCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _settingsViewModel = settingsViewModel;
        _logger = logger;
        _metadataRefresh = new(_dispatcherQueue, RefreshMetadataFilter);
        _metadataScan = new(
            card => card.MetadataLoaded,
            _metadataService.TryGetCached,
            _metadataService.ParseAndCache,
            ApplyMetadata,
            action => _dispatcherQueue.TryEnqueue(() => action()),
            (card, error) => _logger.LogError(
                error is OperationCanceledException ? "CoordinateGallery.ParseMetadataCanceled" : "CoordinateGallery.ParseMetadata",
                error, card.FilePath));
        _metadataScan.PendingCountChanged += count => PendingMetadataCount = count;
        MetadataFilters.PropertyChanged += (_, _) =>
        {
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        };

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.CoordinateResolutionFilterChanged += OnCoordinateResolutionFilterChanged;
    }

    protected override bool CardPassesFilter(object card) =>
        card is CoordinateCard cc && BaseFilterPasses(cc);

    [RelayCommand]
    private Task LoadCardsAsync()
        => RunLoadAsync(async cancellationToken =>
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
                PublishScannedBatch(() =>
                {
                    foreach (var card in batch)
                    {
                        if (!_cardIndex.TryAdd(card.FilePath, card)) continue;
                        Cards.Add(card);
                    }
                }, cancellationToken);
            }, cancellationToken);

            ApplyFilter();
            _cardService.StartWatching(paths);
            StartMetadataScan();
        }, _logger, "CoordinateGallery.LoadCanceled", () => _metadataScan.Cancel());

    private bool _metadataStopping;

    internal Task StopMetadataAsync()
    {
        _metadataStopping = true;
        _metadataRefresh.Stop();
        return _metadataScan.StopAsync(CancellationToken.None);
    }

    private void StartMetadataScan()
    {
        if (_metadataStopping) return;
        _metadataScan.Start(
                Cards,
                ApplyFilter,
                StartMetadataRefreshTimer,
                OnMetadataScanCompleted)
            .Observe(_logger, "CoordinateGallery.ParseMetadata");
    }

    private static void ApplyMetadata(CoordinateCard card, CoordinateMetadata meta)
    {
        card.MetadataSummary = meta.Details;
        card.CoordinateName = meta.CoordinateName ?? string.Empty;
        card.MetadataLoaded = true;
    }

    private void StartMetadataRefreshTimer() => _metadataRefresh.Start();

    private void OnMetadataScanCompleted() => _metadataRefresh.Complete();

    public void RequestThumbnail(
        CoordinateCard card,
        ThumbnailWorkPriority priority = ThumbnailWorkPriority.Prefetch)
        => RequestThumbnailCore(
            card, priority,
            item => _thumbnailCacheService.TryGetCachedPath(item.FilePath, item.DateModified),
            (item, token) => _thumbnailCacheService.EnsureThumbnailAsync(item.FilePath, item.DateModified, token),
            _logger, "CoordinateGallery");

    public void ReleaseThumbnail(CoordinateCard card)
    {
        ReleaseThumbnailRequest(card.FilePath);
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

    public override bool MatchesBrowseCard(CardBase card, IReadOnlyList<string> keywords)
        => card is CoordinateCard coordinate && BaseFilterPasses(coordinate, keywords);

    private bool BaseFilterPasses(CoordinateCard card, IReadOnlyList<string>? keywords = null)
    {
        if (!OriginPasses(card)) return false;

        if (!CardMetadataQuery.MatchesText(card.MetadataSummary, card.FilePath, card.Author?.Name,
            card.MetadataLoaded ? card.CoordinateName : null, keywords ?? _searchKeywords)
            || !MetadataFilters.Matches(card.MetadataLoaded ? card.MetadataSummary : null)) return false;

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

        if (!hasSearch && !filterRes && !HasOriginFilter && MetadataFilters.ActiveCount == 0)
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
        if (_metadataStopping) return;
        _metadataScan.Queue(card, RefreshMetadataFilter, OnMetadataScanCompleted)
            .Observe(_logger, "CoordinateGallery.ParseAddedCardMetadata");
    }

    private void RefreshMetadataFilter()
    {
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
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
        _metadataScan.Dispose();
        _metadataRefresh.Dispose();
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
        _metadataScan.Cancel(resetCount: true);
        _metadataRefresh.Stop();
    }
}

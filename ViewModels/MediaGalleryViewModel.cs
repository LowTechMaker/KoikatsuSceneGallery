using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public partial class MediaGalleryViewModel : GalleryViewModelBase, IDisposable
{
    private readonly MediaCardService _cardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IAppLogger _logger;

    public ObservableCollection<MediaCard> Cards { get; }

    private readonly Dictionary<string, MediaCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? CardRemovedNotification;

    public MediaGalleryViewModel(MediaCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, SettingsViewModel settingsViewModel, ThumbnailPriorityScheduler thumbnailScheduler, IAppLogger logger)
        : base(new ObservableCollection<MediaCard>(), thumbnailScheduler)
    {
        Cards = (ObservableCollection<MediaCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _settingsViewModel = settingsViewModel;
        _logger = logger;
        SelectedSort = SortOption.DateModified;
        SortAscending = false;

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
    }

    protected override bool CardPassesFilter(object card) =>
        card is MediaCard mc && BaseFilterPasses(mc);

    [RelayCommand]
    private Task LoadCardsAsync()
        => RunLoadAsync(async cancellationToken =>
        {
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ShowFileNames = config.ShowFileNames;

            var paths = config.ScreenshotFolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            await _cardService.ScanFoldersAsync(paths,
                batch => PublishScannedCards(batch, _cardIndex, cancellationToken), cancellationToken);

            ApplyFilter();
            _cardService.StartWatching(paths);
        }, _logger, "MediaGallery.LoadCanceled");

    public void RequestThumbnail(
        MediaCard card,
        ThumbnailWorkPriority priority = ThumbnailWorkPriority.Prefetch)
        => RequestThumbnailCore(
            card, priority,
            item => _thumbnailCacheService.TryGetCachedPath(item.FilePath, item.DateModified),
            (item, token) => _thumbnailCacheService.EnsureThumbnailAsync(item.FilePath, item.DateModified, token),
            _logger, "MediaGallery");

    public void ReleaseThumbnail(MediaCard card)
    {
        ReleaseThumbnailRequest(card.FilePath);
    }

    public MediaCard? GetRandomCard() => GetRandomVisibleCard<MediaCard>();

    private bool BaseFilterPasses(MediaCard card)
    {
        foreach (var kw in _searchKeywords)
            if (!card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        var hasSearch = _searchKeywords.Length > 0;

        if (!hasSearch)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not MediaCard card) return false;
                return BaseFilterPasses(card);
            };
        }
        RefreshFilterAndNotify();
    }

    private void OnCardAdded(MediaCard card)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            Cards.Add(card);
            RequestThumbnail(card);
        });
    }

    private void OnCardRemoved(string path)
    {
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
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

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
    private readonly bool _isVideo;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IAppLogger _logger;

    public ObservableCollection<MediaCard> Cards { get; }

    private readonly Dictionary<string, MediaCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? CardRemovedNotification;

    public MediaGalleryViewModel(MediaCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, SettingsViewModel settingsViewModel, IAppLogger logger, bool isVideo)
        : base(new ObservableCollection<MediaCard>())
    {
        Cards = (ObservableCollection<MediaCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _settingsViewModel = settingsViewModel;
        _logger = logger;
        _isVideo = isVideo;

        if (!_isVideo)
        {
            SelectedSort = SortOption.DateModified;
            SortAscending = false;
        }

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
    }

    protected override bool CardPassesFilter(object card) =>
        card is MediaCard mc && BaseFilterPasses(mc);

    [RelayCommand]
    private async Task LoadCardsAsync()
    {
        var cancellationToken = BeginLoad();
        ResetThumbnailState();

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ShowFileNames = config.ShowFileNames;

            var paths = _isVideo ? config.VideoFolderPaths : config.ScreenshotFolderPaths;
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
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogError("MediaGallery.LoadCanceled", ex);
        }
        finally
        {
            if (_loadCts?.Token == cancellationToken)
                IsLoading = false;
            RaiseCardsReloaded();
        }
    }

    public void RequestThumbnail(MediaCard card)
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
            .Observe(_logger, "MediaGallery.GenerateThumbnail");
    }

    public void ReleaseThumbnail(MediaCard card)
    {
    }

    private async Task GenerateOneAsync(MediaCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (card.HasThumbnail) return;

                var thumbnailPath = _isVideo
                    ? await _thumbnailCacheService.EnsureVideoThumbnailAsync(card.FilePath, card.DateModified, cancellationToken).ConfigureAwait(false)
                    : await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified, cancellationToken).ConfigureAwait(false);

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
        catch (OperationCanceledException ex) { _logger.LogError("MediaGallery.GenerateThumbnailCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("MediaGallery.GenerateThumbnail", ex, card.FilePath); }
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

    public MediaCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as MediaCard;
    }

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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

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
    private readonly bool _isVideo;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IAppLogger _logger;

    public ObservableCollection<MediaCard> Cards { get; }

    private readonly Dictionary<string, MediaCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? CardRemovedNotification;

    public MediaGalleryViewModel(MediaCardService cardService, SettingsService settingsService, SettingsViewModel settingsViewModel, IAppLogger logger, bool isVideo)
        : base(new ObservableCollection<MediaCard>())
    {
        Cards = (ObservableCollection<MediaCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
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
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

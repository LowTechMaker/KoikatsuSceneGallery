using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public partial class MediaGalleryViewModel : ObservableObject, IDisposable
{
    private readonly MediaCardService _cardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly bool _isVideo;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<MediaCard> Cards { get; } = [];
    public AdvancedCollectionView CardsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShuffleMode))]
    public partial SortOption SelectedSort { get; set; } = SortOption.Name;

    [ObservableProperty]
    public partial bool SortAscending { get; set; } = true;

    public bool IsShuffleMode => SelectedSort == SortOption.Shuffle;

    private int _shuffleCount;
    private readonly HashSet<object> _shuffleSet = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsLoading { get; set; }

    public bool IsEmpty => !IsLoading && CardsView.Count == 0;

    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneratingThumbnails))]
    public partial int PendingThumbnailCount { get; set; }

    public bool IsGeneratingThumbnails => PendingThumbnailCount > 0;

    private readonly Dictionary<string, MediaCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbnailCts;
    private readonly SemaphoreSlim _thumbnailGate = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _thumbnailPathCache = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string>? CardRemovedNotification;
    public event Action? CardsReloaded;

    public MediaGalleryViewModel(MediaCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, bool isVideo)
    {
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _isVideo = isVideo;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        CardsView = new AdvancedCollectionView(Cards, true);
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));

        if (!_isVideo)
        {
            SelectedSort = SortOption.DateModified;
            SortAscending = false;
        }

        ApplySort();

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        App.SettingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedSortChanged(SortOption value) { ApplySort(); ApplyFilter(); }
    partial void OnSortAscendingChanged(bool value) => ApplySort();

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
            ShowFileNames = config.ShowFileNames;

            var paths = _isVideo ? config.VideoFolderPaths : config.ScreenshotFolderPaths;
            Cards.Clear();
            _cardIndex.Clear();

            await _cardService.ScanFoldersAsync(paths, batch =>
            {
                var processed = new ManualResetEventSlim(false);
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    var added = new List<MediaCard>(batch.Count);
                    using (CardsView.DeferRefresh())
                    {
                        foreach (var card in batch)
                        {
                            if (!_cardIndex.TryAdd(card.FilePath, card)) continue;
                            Cards.Add(card);
                            added.Add(card);
                        }
                    }
                    foreach (var card in added)
                        RequestThumbnail(card);
                    processed.Set();
                });
                processed.Wait();
                processed.Dispose();
            });

            ApplyFilter();
            _cardService.StartWatching(paths);
        }
        finally
        {
            IsLoading = false;
            CardsReloaded?.Invoke();
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

        if (!_thumbnailRequested.Add(card.FilePath)) return;

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        PendingThumbnailCount++;
        _ = Task.Run(() => GenerateOneAsync(card, token));
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
                    ? await _thumbnailCacheService.EnsureVideoThumbnailAsync(card.FilePath, card.DateModified).ConfigureAwait(false)
                    : await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified).ConfigureAwait(false);

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
        catch (OperationCanceledException) { }
        catch (Exception) { }
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

    private void OnShowFileNamesSettingChanged(bool value)
    {
        _dispatcherQueue.TryEnqueue(() => ShowFileNames = value);
    }

    public void SetShuffleCount(int count)
    {
        _shuffleCount = count;
        if (IsShuffleMode) ApplyFilter();
    }

    public void Reshuffle() => ApplyFilter();

    private void ApplySort()
    {
        using (CardsView.DeferRefresh())
        {
            CardsView.SortDescriptions.Clear();
            if (SelectedSort == SortOption.Shuffle) return;
            var direction = SortAscending ? SortDirection.Ascending : SortDirection.Descending;
            string propertyName = SelectedSort switch
            {
                SortOption.Name => nameof(MediaCard.FileName),
                SortOption.DateModified => nameof(MediaCard.DateModified),
                SortOption.FileSize => nameof(MediaCard.FileSize),
                _ => nameof(MediaCard.FileName)
            };

            CardsView.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }
    }

    public MediaCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as MediaCard;
    }

    private void ApplyFilter()
    {
        var keywords = SearchText
            .Split(',')
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToArray();
        var hasSearch = keywords.Length > 0;
        var isShuffleMode = IsShuffleMode;

        Func<MediaCard, bool> baseFilter = card =>
        {
            if (hasSearch)
            {
                foreach (var kw in keywords)
                {
                    if (!card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            return true;
        };

        if (isShuffleMode && _shuffleCount > 0)
        {
            var candidates = new List<MediaCard>();
            foreach (var card in Cards)
                if (baseFilter(card))
                    candidates.Add(card);

            _shuffleSet.Clear();
            int n = Math.Min(_shuffleCount, candidates.Count);
            for (int i = 0; i < n; i++)
            {
                int j = Random.Shared.Next(i, candidates.Count);
                (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
                _shuffleSet.Add(candidates[i]);
            }
        }

        if (!hasSearch && !isShuffleMode)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not MediaCard card) return false;
                if (!baseFilter(card)) return false;
                if (isShuffleMode && _shuffleSet.Count > 0 && !_shuffleSet.Contains(card)) return false;
                return true;
            };
        }
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async void OnCardAdded(MediaCard card)
    {
        var thumbnailPath = _isVideo
            ? await _thumbnailCacheService.EnsureVideoThumbnailAsync(card.FilePath, card.DateModified)
            : await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified);
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            card.ThumbnailPath = thumbnailPath;
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
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        App.SettingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

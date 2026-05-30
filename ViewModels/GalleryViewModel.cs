using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public enum SortOption
{
    Name,
    DateModified,
    FileSize
}

public partial class GalleryViewModel : ObservableObject, IDisposable
{
    private readonly SceneCardService _sceneCardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<SceneCard> Cards { get; } = [];
    public AdvancedCollectionView CardsView { get; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial SortOption SelectedSort { get; set; } = SortOption.Name;

    [ObservableProperty]
    public partial bool SortAscending { get; set; } = true;

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

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];
    private readonly Dictionary<string, SceneCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbnailCts;
    private readonly SemaphoreSlim _thumbnailGate = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);

    public GalleryViewModel(SceneCardService sceneCardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService)
    {
        _sceneCardService = sceneCardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        CardsView = new AdvancedCollectionView(Cards, true);
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        ApplySort();

        _sceneCardService.CardAdded += OnCardAdded;
        _sceneCardService.CardRemoved += OnCardRemoved;

        App.SettingsViewModel.ResolutionFilterChanged += OnResolutionFilterChanged;
        App.SettingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedSortChanged(SortOption value)
    {
        ApplySort();
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
        PendingThumbnailCount = 0;

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            _resolutionFilterEnabled = config.ResolutionFilterEnabled;
            _allowedResolutions = [.. config.AllowedResolutions];
            ShowFileNames = config.ShowFileNames;

            var paths = config.FolderPaths;
            var cards = await _sceneCardService.ScanFoldersAsync(paths);

            Cards.Clear();
            _cardIndex.Clear();
            using (CardsView.DeferRefresh())
            {
                foreach (var card in cards)
                {
                    Cards.Add(card);
                    _cardIndex[card.FilePath] = card;
                }
            }
            ApplyFilter();
            _sceneCardService.StartWatching(paths);
        }
        finally
        {
            IsLoading = false;
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
        if (!_thumbnailRequested.Add(card.FilePath)) return;

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        PendingThumbnailCount++;
        _ = Task.Run(() => GenerateOneAsync(card, token));
    }

    private async Task GenerateOneAsync(SceneCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card).ConfigureAwait(false);
                if (thumbnailPath != null && !cancellationToken.IsCancellationRequested)
                    _dispatcherQueue.TryEnqueue(() => card.ThumbnailPath = thumbnailPath);
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
                if (!cancellationToken.IsCancellationRequested)
                    PendingThumbnailCount--;
            });
        }
    }

    private void OnResolutionFilterChanged(bool enabled, HashSet<string> resolutions)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            _resolutionFilterEnabled = enabled;
            _allowedResolutions = resolutions;
            ApplyFilter();
        });
    }

    private void OnShowFileNamesSettingChanged(bool value)
    {
        _dispatcherQueue.TryEnqueue(() => ShowFileNames = value);
    }

    private void ApplySort()
    {
        using (CardsView.DeferRefresh())
        {
            CardsView.SortDescriptions.Clear();
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

    public SceneCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as SceneCard;
    }

    private void ApplyFilter()
    {
        var keywords = SearchText
            .Split(',')
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToArray();
        var hasSearch = keywords.Length > 0;
        var filterRes = _resolutionFilterEnabled && _allowedResolutions.Count > 0;

        if (!hasSearch && !filterRes)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not SceneCard card) return false;

                if (hasSearch)
                {
                    foreach (var kw in keywords)
                    {
                        if (!card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase))
                            return false;
                    }
                }

                if (filterRes && !_allowedResolutions.Contains(card.Resolution))
                    return false;

                return true;
            };
        }
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async void OnCardAdded(SceneCard card)
    {
        var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card);
        _dispatcherQueue.TryEnqueue(() =>
        {
            card.ThumbnailPath = thumbnailPath;
            Cards.Add(card);
            _cardIndex[card.FilePath] = card;
        });
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
        _thumbnailCts?.Cancel();
        _thumbnailCts?.Dispose();
        _sceneCardService.CardAdded -= OnCardAdded;
        _sceneCardService.CardRemoved -= OnCardRemoved;
        App.SettingsViewModel.ResolutionFilterChanged -= OnResolutionFilterChanged;
        App.SettingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

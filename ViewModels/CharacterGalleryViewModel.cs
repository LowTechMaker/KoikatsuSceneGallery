using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>Source/version filter for the character gallery (All + the <see cref="CardSource"/> values).</summary>
public enum CardSourceFilterOption
{
    All,
    KoikatsuSunshine,
    KoikatsuHF,
    Madevil,
    Unknown
}

/// <summary>
/// Backs the character-card gallery: scan + display + sort + search (filename or
/// character name) + on-demand thumbnails + a source/version filter. Embedded
/// metadata is parsed on background threads and cached, mirroring the scene
/// gallery's metadata pipeline. Reuses <see cref="SortOption"/>.
/// </summary>
public partial class CharacterGalleryViewModel : ObservableObject, IDisposable
{
    private readonly CharacterCardService _cardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly CharacterMetadataService _metadataService;
    private readonly DispatcherQueue _dispatcherQueue;

    public ObservableCollection<CharacterCard> Cards { get; } = [];
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;

    [ObservableProperty]
    public partial CardSourceFilterOption SourceFilter { get; set; } = CardSourceFilterOption.All;

    private bool HasSourceFilter => SourceFilter != CardSourceFilterOption.All;
    private bool NeedsLiveRefresh => HasSourceFilter || !string.IsNullOrWhiteSpace(SearchText);

    private readonly Dictionary<string, CharacterCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _thumbnailCts;
    private readonly SemaphoreSlim _thumbnailGate = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _metadataCts;
    // I/O-bound like scenes: small fixed concurrency keeps the first scan gentle
    // on spinning disks while being plenty on an SSD.
    private const int MetadataParseConcurrency = 4;
    private readonly SemaphoreSlim _metadataGate = new(MetadataParseConcurrency);
    private DispatcherQueueTimer? _metadataRefreshTimer;

    public CharacterGalleryViewModel(CharacterCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, CharacterMetadataService metadataService)
    {
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        CardsView = new AdvancedCollectionView(Cards, true);
        Cards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        ApplySort();

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        App.SettingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSourceFilterChanged(CardSourceFilterOption value) => ApplyFilter();
    partial void OnSelectedSortChanged(SortOption value) => ApplySort();
    partial void OnSortAscendingChanged(bool value) => ApplySort();

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
            ShowFileNames = config.ShowFileNames;

            var paths = config.CharacterFolderPaths;
            var cards = await _cardService.ScanFoldersAsync(paths);

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
            _cardService.StartWatching(paths);
            StartMetadataScan();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Parses metadata for every card that doesn't have it yet. Cache hits apply
    /// immediately; misses are parsed on background threads with bounded
    /// concurrency. While a source filter or search is active the view refreshes
    /// on a throttled timer so newly classified cards fold in smoothly.
    /// </summary>
    private void StartMetadataScan()
    {
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataCts = new CancellationTokenSource();
        var token = _metadataCts.Token;
        PendingMetadataCount = 0;

        var pending = new List<CharacterCard>();
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
            if (NeedsLiveRefresh) ApplyFilter();
            return;
        }

        PendingMetadataCount = pending.Count;
        StartMetadataRefreshTimer();
        foreach (var card in pending)
            _ = Task.Run(() => ParseMetadataAsync(card, token), token);
    }

    private async Task ParseMetadataAsync(CharacterCard card, CancellationToken cancellationToken)
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

    private static void ApplyMetadata(CharacterCard card, CharacterMetadata meta)
    {
        card.CharacterName = meta.FullName;
        card.Game = meta.Game;
        card.IsMadevil = meta.IsMadevil;
        card.Source = meta.Source;
        // Set last so a filter refresh observing this flag sees final values.
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
        if (NeedsLiveRefresh)
        {
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    private void OnMetadataScanCompleted()
    {
        _metadataRefreshTimer?.Stop();
        if (NeedsLiveRefresh)
        {
            CardsView.RefreshFilter();
            OnPropertyChanged(nameof(IsEmpty));
        }
    }

    /// <summary>
    /// Requests a thumbnail for a single card on demand (called as cards scroll
    /// into view).
    /// </summary>
    public void RequestThumbnail(CharacterCard card)
    {
        if (card.HasThumbnail) return;
        if (!_thumbnailRequested.Add(card.FilePath)) return;

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        PendingThumbnailCount++;
        _ = Task.Run(() => GenerateOneAsync(card, token));
    }

    private async Task GenerateOneAsync(CharacterCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified).ConfigureAwait(false);
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
            _dispatcherQueue.TryEnqueue(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                    PendingThumbnailCount--;
            });
        }
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
                SortOption.Name => nameof(CharacterCard.FileName),
                SortOption.DateModified => nameof(CharacterCard.DateModified),
                SortOption.FileSize => nameof(CharacterCard.FileSize),
                _ => nameof(CharacterCard.FileName)
            };

            CardsView.SortDescriptions.Add(new SortDescription(propertyName, direction));
        }
    }

    public CharacterCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as CharacterCard;
    }

    private void ApplyFilter()
    {
        var keywords = SearchText
            .Split(',')
            .Select(k => k.Trim())
            .Where(k => k.Length > 0)
            .ToArray();
        var hasSearch = keywords.Length > 0;
        var sourceFilter = SourceFilter;
        var hasSourceFilter = sourceFilter != CardSourceFilterOption.All;

        if (!hasSearch && !hasSourceFilter)
        {
            CardsView.Filter = null!;
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not CharacterCard card) return false;

                if (hasSearch)
                {
                    foreach (var kw in keywords)
                    {
                        bool inPath = card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase);
                        bool inName = card.MetadataLoaded
                            && card.CharacterName.Contains(kw, StringComparison.OrdinalIgnoreCase);
                        if (!inPath && !inName) return false;
                    }
                }

                if (hasSourceFilter)
                {
                    // Not classified yet — hide until parsed (reappears via the
                    // throttled refresh once metadata is ready).
                    if (!card.MetadataLoaded) return false;
                    var target = sourceFilter switch
                    {
                        CardSourceFilterOption.KoikatsuSunshine => CardSource.KoikatsuSunshine,
                        CardSourceFilterOption.KoikatsuHF => CardSource.KoikatsuHF,
                        CardSourceFilterOption.Madevil => CardSource.Madevil,
                        _ => CardSource.Unknown
                    };
                    if (card.Source != target) return false;
                }

                return true;
            };
        }
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async void OnCardAdded(CharacterCard card)
    {
        var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified);
        _dispatcherQueue.TryEnqueue(() =>
        {
            card.ThumbnailPath = thumbnailPath;
            Cards.Add(card);
            _cardIndex[card.FilePath] = card;
            QueueMetadata(card);
        });
    }

    private void QueueMetadata(CharacterCard card)
    {
        if (card.MetadataLoaded) return;
        if (_metadataService.TryGetCached(card, out var meta))
        {
            ApplyMetadata(card, meta);
            if (NeedsLiveRefresh)
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
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataRefreshTimer?.Stop();
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        App.SettingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        GC.SuppressFinalize(this);
    }
}

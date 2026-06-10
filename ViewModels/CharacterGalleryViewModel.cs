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
    private bool HasResolutionFilter => _resolutionFilterEnabled && _allowedResolutions.Count > 0;
    private bool NeedsLiveRefresh => true;

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];

    private readonly Dictionary<string, CharacterCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CharacterCard>> _versionIndex = new();
    private CancellationTokenSource? _thumbnailCts;
    private readonly SemaphoreSlim _thumbnailGate = new(Math.Max(1, Environment.ProcessorCount - 1));
    private readonly HashSet<string> _thumbnailRequested = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _thumbnailPathCache = new(StringComparer.OrdinalIgnoreCase);

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
        App.SettingsViewModel.CharacterResolutionFilterChanged += OnCharacterResolutionFilterChanged;
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
        _thumbnailPathCache.Clear();
        PendingThumbnailCount = 0;

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            ShowFileNames = config.ShowFileNames;
            _resolutionFilterEnabled = config.CharacterResolutionFilterEnabled;
            _allowedResolutions = [.. config.CharacterAllowedResolutions];

            var paths = config.CharacterFolderPaths;
            Cards.Clear();
            _cardIndex.Clear();
            _versionIndex.Clear();

            await _cardService.ScanFoldersAsync(paths, batch =>
            {
                var processed = new ManualResetEventSlim(false);
                // Low priority so the parallel scan's tight batch burst can't
                // starve user input/rendering during the initial load.
                _dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    var added = new List<CharacterCard>(batch.Count);
                    using (CardsView.DeferRefresh())
                    {
                        foreach (var card in batch)
                        {
                            // Skip files already loaded so the same file can't
                            // appear twice in the grid.
                            if (!_cardIndex.TryAdd(card.FilePath, card)) continue;
                            Cards.Add(card);
                            added.Add(card);
                        }
                    }
                    // Request thumbnails for every loaded card, not just ones the
                    // GridView happens to realize (ContainerContentChanging
                    // doesn't reliably re-fire when sort/filter shifts items
                    // among realized containers). Gated + disk-cached.
                    foreach (var card in added)
                        RequestThumbnail(card);
                    processed.Set();
                });
                processed.Wait();
                processed.Dispose();
            });

            ApplyFilter();
            _cardService.StartWatching(paths);
            StartMetadataScan();
        }
        finally
        {
            IsLoading = false;
            CardsReloaded?.Invoke();
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
            {
                ApplyMetadata(card, meta);
                UpdateVersionIndex(card);
            }
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
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        ApplyMetadata(card, meta);
                        UpdateVersionIndex(card);
                    });
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

    public event Action<string>? VersionIndexChanged;
    public event Action? CardsReloaded;

    private void UpdateVersionIndex(CharacterCard card)
    {
        if (string.IsNullOrWhiteSpace(card.CharacterName)) return;

        if (!_versionIndex.TryGetValue(card.CharacterName, out var group))
        {
            group = [];
            _versionIndex[card.CharacterName] = group;
        }
        if (!group.Contains(card))
            group.Add(card);

        group.Sort((a, b) => b.FileTimestamp.CompareTo(a.FileTimestamp));

        var count = group.Count;
        for (int i = 0; i < count; i++)
        {
            group[i].VersionCount = count;
            group[i].IsLatestVersion = i == 0;
        }

        VersionIndexChanged?.Invoke(card.CharacterName);
    }

    private void RemoveFromVersionIndex(CharacterCard card)
    {
        if (string.IsNullOrWhiteSpace(card.CharacterName)) return;
        if (!_versionIndex.TryGetValue(card.CharacterName, out var group)) return;

        group.Remove(card);
        if (group.Count == 0)
        {
            _versionIndex.Remove(card.CharacterName);
            VersionIndexChanged?.Invoke(card.CharacterName);
            return;
        }

        var count = group.Count;
        for (int i = 0; i < count; i++)
        {
            group[i].VersionCount = count;
            group[i].IsLatestVersion = i == 0;
        }

        VersionIndexChanged?.Invoke(card.CharacterName);
    }

    public List<CharacterCard>? GetVersions(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return null;
        return _versionIndex.TryGetValue(characterName, out var group) && group.Count > 1
            ? group
            : null;
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
        if (_thumbnailPathCache.TryGetValue(card.FilePath, out var cached))
        {
            card.ThumbnailPath = cached;
            return;
        }
        if (!_thumbnailRequested.Add(card.FilePath)) return;

        var token = _thumbnailCts?.Token ?? CancellationToken.None;
        PendingThumbnailCount++;
        _ = Task.Run(() => GenerateOneAsync(card, token));
    }

    // Eviction-on-recycle was removed: clearing ThumbnailPath when a container
    // recycled raced with re-prepare during scroll/relayout and left visible
    // cards blank or stale. Thumbnails now stay loaded once generated.
    public void ReleaseThumbnail(CharacterCard card)
    {
    }

    private async Task GenerateOneAsync(CharacterCard card, CancellationToken cancellationToken)
    {
        try
        {
            await _thumbnailGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (card.HasThumbnail) return;
                var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified).ConfigureAwait(false);
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

    private void OnCharacterResolutionFilterChanged(bool enabled, HashSet<string> resolutions)
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
        var filterRes = _resolutionFilterEnabled && _allowedResolutions.Count > 0;

        CardsView.Filter = item =>
        {
            if (item is not CharacterCard card) return false;

            if (!card.IsLatestVersion) return false;

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

            if (filterRes && !_allowedResolutions.Contains(card.Resolution))
                return false;

            if (hasSourceFilter)
            {
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
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private async void OnCardAdded(CharacterCard card)
    {
        var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(card.FilePath, card.DateModified);
        _dispatcherQueue.TryEnqueue(() =>
        {
            // Guard against re-adding a file that's already present.
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            card.ThumbnailPath = thumbnailPath;
            Cards.Add(card);
            QueueMetadata(card);
        });
    }

    private void QueueMetadata(CharacterCard card)
    {
        if (card.MetadataLoaded) return;
        if (_metadataService.TryGetCached(card, out var meta))
        {
            ApplyMetadata(card, meta);
            UpdateVersionIndex(card);
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
            {
                RemoveFromVersionIndex(existing);
                Cards.Remove(existing);
            }
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
        App.SettingsViewModel.CharacterResolutionFilterChanged -= OnCharacterResolutionFilterChanged;
        GC.SuppressFinalize(this);
    }
}

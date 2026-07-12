using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

public enum CardSourceFilterOption
{
    All,
    KoikatsuSunshine,
    KoikatsuHF,
    Madevil,
    Unknown
}

public partial class CharacterGalleryViewModel : GalleryViewModelBase, IDisposable
{
    private readonly CharacterCardService _cardService;
    private readonly SettingsService _settingsService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly CharacterMetadataService _metadataService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly IAppLogger _logger;

    public ObservableCollection<CharacterCard> Cards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsingMetadata))]
    public partial int PendingMetadataCount { get; set; }

    public bool IsParsingMetadata => PendingMetadataCount > 0;

    [ObservableProperty]
    public partial CardSourceFilterOption SourceFilter { get; set; } = CardSourceFilterOption.All;

    private bool HasSourceFilter => SourceFilter != CardSourceFilterOption.All;
    private bool HasResolutionFilter => _resolutionFilterEnabled && _allowedResolutions.Count > 0;

    private bool _resolutionFilterEnabled;
    private HashSet<string> _allowedResolutions = [];

    private readonly Dictionary<string, CharacterCard> _cardIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<CharacterCard>> _versionIndex = new();

    private CancellationTokenSource? _metadataCts;
    private const int MetadataParseConcurrency = 4;
    private DispatcherQueueTimer? _metadataRefreshTimer;

    public event Action<string>? VersionIndexChanged;

    public CharacterGalleryViewModel(CharacterCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, CharacterMetadataService metadataService, SettingsViewModel settingsViewModel, IAppLogger logger)
        : base(new ObservableCollection<CharacterCard>())
    {
        Cards = (ObservableCollection<CharacterCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _thumbnailCacheService = thumbnailCacheService;
        _metadataService = metadataService;
        _settingsViewModel = settingsViewModel;
        _logger = logger;

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.CharacterResolutionFilterChanged += OnCharacterResolutionFilterChanged;
    }

    protected override bool CardPassesFilter(object card) =>
        card is CharacterCard cc && BaseFilterPasses(cc);

    partial void OnSourceFilterChanged(CardSourceFilterOption value)
    {
        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

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
            _resolutionFilterEnabled = config.CharacterResolutionFilterEnabled;
            _allowedResolutions = [.. config.CharacterAllowedResolutions];

            var paths = config.CharacterFolderPaths;
            Cards.Clear();
            _cardIndex.Clear();
            _versionIndex.Clear();

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
            _logger.LogError("CharacterGallery.LoadCanceled", ex);
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
            .Observe(_logger, "CharacterGallery.ParseMetadata");
    }

    private async Task ParseMetadataAsync(CharacterCard card, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var meta = _metadataService.ParseAndCache(card, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ApplyMetadata(card, meta);
                    UpdateVersionIndex(card);
                });
        }
        catch (OperationCanceledException ex) { _logger.LogError("CharacterGallery.ParseMetadataCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("CharacterGallery.ParseMetadata", ex, card.FilePath); }
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
        card.MetadataLoaded = true;
    }

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
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void OnMetadataScanCompleted()
    {
        _metadataRefreshTimer?.Stop();
        CardsView.RefreshFilter();
        OnPropertyChanged(nameof(IsEmpty));
    }

    public void RequestThumbnail(CharacterCard card)
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
            .Observe(_logger, "CharacterGallery.GenerateThumbnail");
    }

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
        catch (OperationCanceledException ex) { _logger.LogError("CharacterGallery.GenerateThumbnailCanceled", ex, card.FilePath); }
        catch (Exception ex) { _logger.LogError("CharacterGallery.GenerateThumbnail", ex, card.FilePath); }
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
            if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
            ApplyFilter();
        });
    }

    public CharacterCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as CharacterCard;
    }

    private bool BaseFilterPasses(CharacterCard card)
    {
        if (!card.IsLatestVersion) return false;

        foreach (var kw in _searchKeywords)
        {
            bool inPath = card.FilePath.Contains(kw, StringComparison.OrdinalIgnoreCase);
            bool inName = card.MetadataLoaded
                && card.CharacterName.Contains(kw, StringComparison.OrdinalIgnoreCase);
            if (!inPath && !inName) return false;
        }

        if (_resolutionFilterEnabled && _allowedResolutions.Count > 0
            && !_allowedResolutions.Contains(card.Resolution))
            return false;

        if (SourceFilter != CardSourceFilterOption.All)
        {
            if (!card.MetadataLoaded) return false;
            var target = SourceFilter switch
            {
                CardSourceFilterOption.KoikatsuSunshine => CardSource.KoikatsuSunshine,
                CardSourceFilterOption.KoikatsuHF => CardSource.KoikatsuHF,
                CardSourceFilterOption.Madevil => CardSource.Madevil,
                _ => CardSource.Unknown
            };
            if (card.Source != target) return false;
        }

        return true;
    }

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        var hasSearch = _searchKeywords.Length > 0;
        var filterRes = _resolutionFilterEnabled && _allowedResolutions.Count > 0;
        var hasSourceFilter = SourceFilter != CardSourceFilterOption.All;

        if (!hasSearch && !filterRes && !hasSourceFilter)
        {
            CardsView.Filter = item =>
            {
                if (item is not CharacterCard card) return false;
                return card.IsLatestVersion;
            };
        }
        else
        {
            CardsView.Filter = item =>
            {
                if (item is not CharacterCard card) return false;
                return BaseFilterPasses(card);
            };
        }
        RefreshFilterAndNotify();
    }

    private void OnCardAdded(CharacterCard card)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_cardIndex.TryAdd(card.FilePath, card)) return;
            Cards.Add(card);
            RequestThumbnail(card);
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
            .Observe(_logger, "CharacterGallery.ParseAddedCardMetadata");
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
        _settingsViewModel.CharacterResolutionFilterChanged -= OnCharacterResolutionFilterChanged;
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

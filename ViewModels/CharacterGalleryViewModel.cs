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
    public MetadataFilterState MetadataFilters { get; } = new();

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
    // Case-insensitive to match _cardIndex above; safe only because removal
    // now reads CharacterCard.IndexedVersionKey rather than re-deriving the key.
    private readonly Dictionary<string, List<CharacterCard>> _versionIndex =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly MetadataScanCoordinator<CharacterCard, CharacterMetadata> _metadataScan;
    private readonly GalleryMetadataRefresh _metadataRefresh;

    public event Action<string>? VersionIndexChanged;

    public CharacterGalleryViewModel(CharacterCardService cardService, SettingsService settingsService, ThumbnailCacheService thumbnailCacheService, CharacterMetadataService metadataService, SettingsViewModel settingsViewModel, ThumbnailPriorityScheduler thumbnailScheduler, IAppLogger logger)
        : base(new ObservableCollection<CharacterCard>(), thumbnailScheduler)
    {
        Cards = (ObservableCollection<CharacterCard>)_cardsSource;
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
            (card, meta) => { ApplyMetadata(card, meta); UpdateVersionIndex(card); },
            action => _dispatcherQueue.TryEnqueue(() => action()),
            (card, error) => _logger.LogError(
                error is OperationCanceledException ? "CharacterGallery.ParseMetadataCanceled" : "CharacterGallery.ParseMetadata",
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
    private Task LoadCardsAsync()
        => RunLoadAsync(async cancellationToken =>
        {
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ShowFileNames = config.ShowFileNames;
            _resolutionFilterEnabled = config.CharacterResolutionFilterEnabled;
            _allowedResolutions = [.. config.CharacterAllowedResolutions];

            var paths = config.CharacterFolderPaths;
            // A reload is the moment an edit made outside the app should show up.
            App.Services.GetService<CharacterAnnotationStore>()?.ClearCache();
            Cards.Clear();
            _cardIndex.Clear();
            _versionIndex.Clear();

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
        }, _logger, "CharacterGallery.LoadCanceled", () => _metadataScan.Cancel());

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
            .Observe(_logger, "CharacterGallery.ParseMetadata");
    }

    private static void ApplyMetadata(CharacterCard card, CharacterMetadata meta)
    {
        card.MetadataSummary = meta.Details;
        card.CharacterName = meta.FullName;
        card.Game = meta.Game;
        card.IsMadevil = meta.IsMadevil;
        card.Source = meta.Source;
        card.MetadataLoaded = true;
    }

    /// <summary>
    /// The character a card is filed under: its own name, unless the user
    /// attached it to another character.
    /// </summary>
    private static string? EffectiveVersionKey(CharacterCard card)
        => card.CharacterGroupKey is { Length: > 0 } group
            ? group
            : (string.IsNullOrWhiteSpace(card.CharacterName) ? null : card.CharacterName);

    private void UpdateVersionIndex(CharacterCard card)
    {
        var key = EffectiveVersionKey(card);
        if (key is null) return;

        // The key can change under a card: metadata may re-parse, or the user
        // may attach it to a different character. Leave the old group first.
        if (card.IndexedVersionKey is { } previous
            && !string.Equals(previous, key, StringComparison.OrdinalIgnoreCase))
        {
            RemoveFromVersionIndex(card);
        }

        if (!_versionIndex.TryGetValue(key, out var group))
        {
            group = [];
            _versionIndex[key] = group;
        }
        if (!group.Contains(card))
            group.Add(card);

        // Store the key as looked up rather than hunting for the dictionary's
        // own spelling: the index compares case-insensitively, so this finds
        // the same group, and searching the key set would be O(characters) on
        // every card of every scan.
        card.IndexedVersionKey = key;

        RankGroup(group);

        VersionIndexChanged?.Invoke(card.IndexedVersionKey);
    }

    /// <summary>
    /// Reorders one character's cards, marks the one that represents it, and
    /// tells every card how many live what-if versions the character has.
    /// </summary>
    private static void RankGroup(List<CharacterCard> group)
    {
        var primary = CharacterVersionRanker.SortAndFindPrimary(
            group,
            card => card.FileTimestamp,
            card => card.VersionKind,
            card => card.IsSuperseded);

        var alternates = group.Count(card =>
            CharacterVersionRanker.CountsAsLiveAlternate(card.VersionKind, card.IsSuperseded));

        for (var i = 0; i < group.Count; i++)
        {
            group[i].VersionCount = group.Count;
            group[i].IsLatestVersion = i == primary;
            group[i].AlternateVersionCount = alternates;
        }
    }

    private void RemoveFromVersionIndex(CharacterCard card)
    {
        if (card.IndexedVersionKey is not { } key) return;
        if (!_versionIndex.TryGetValue(key, out var group)) return;

        group.Remove(card);
        card.IndexedVersionKey = null;

        if (group.Count == 0)
        {
            _versionIndex.Remove(key);
            VersionIndexChanged?.Invoke(key);
            return;
        }

        RankGroup(group);

        VersionIndexChanged?.Invoke(key);
    }

    /// <summary>
    /// The cards filed under one character, or null when there is only one.
    /// A copy: the index's own list is reordered as cards arrive.
    /// </summary>
    public List<CharacterCard>? GetVersions(string characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName)) return null;
        return _versionIndex.TryGetValue(characterName, out var group) && group.Count > 1
            ? [.. group]
            : null;
    }

    /// <summary>
    /// Records the user's annotation for one card and re-files it.
    /// </summary>
    /// <remarks>
    /// One card in, one card out: the index is adjusted in place rather than
    /// rebuilt, so annotating a card never rescans the library.
    /// </remarks>
    public async Task ApplyAnnotationAsync(
        CharacterCard card,
        CharacterVersionKind kind,
        bool superseded,
        string? note,
        string? groupKey)
    {
        ArgumentNullException.ThrowIfNull(card);

        var store = App.Services.GetService<CharacterAnnotationStore>();
        if (store is not null)
            await store.UpdateAsync(card.FilePath, kind, superseded, note, groupKey).ConfigureAwait(true);

        RemoveFromVersionIndex(card);
        card.VersionKind = kind;
        card.IsSuperseded = superseded;
        card.VersionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        card.CharacterGroupKey = string.IsNullOrWhiteSpace(groupKey) ? null : groupKey.Trim();
        UpdateVersionIndex(card);

        if (IsShuffleMode) { BuildShuffleQueue(); ApplySort(); }
        ApplyFilter();
    }

    /// <summary>The key a card is filed under, for callers that follow the index.</summary>
    public string? GetVersionKey(CharacterCard card) => card.IndexedVersionKey;

    /// <summary>
    /// Every character in the index with its card count, each flagged with
    /// whether it belongs to <paramref name="author"/>.
    /// </summary>
    /// <remarks>
    /// The flag rather than a filter: versions of a character come from the
    /// same author, so the picker leads with those, but a card can legitimately
    /// be a variant of someone else's character, so the rest stay reachable.
    /// </remarks>
    public List<(string Key, int Count, bool SameAuthor)> GetVersionGroupKeys(AuthorDisplay? author = null)
        => [.. _versionIndex
            .Select(pair => (
                pair.Key,
                pair.Value.Count,
                SameAuthor: author is not null
                            && pair.Value.Any(card => card.Author?.Key == author.Key)))
            .OrderBy(entry => entry.Key, StringComparer.CurrentCulture)];

    private void StartMetadataRefreshTimer() => _metadataRefresh.Start();

    private void OnMetadataScanCompleted() => _metadataRefresh.Complete();

    public void RequestThumbnail(
        CharacterCard card,
        ThumbnailWorkPriority priority = ThumbnailWorkPriority.Prefetch)
        => RequestThumbnailCore(
            card, priority,
            item => _thumbnailCacheService.TryGetCachedPath(item.FilePath, item.DateModified),
            (item, token) => _thumbnailCacheService.EnsureThumbnailAsync(item.FilePath, item.DateModified, token),
            _logger, "CharacterGallery");

    public void ReleaseThumbnail(CharacterCard card)
    {
        ReleaseThumbnailRequest(card.FilePath);
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

    public override bool MatchesBrowseCard(CardBase card, IReadOnlyList<string> keywords)
        => card is CharacterCard character && BaseFilterPasses(character, keywords);

    private bool BaseFilterPasses(CharacterCard card, IReadOnlyList<string>? keywords = null)
    {
        var terms = keywords ?? _searchKeywords;

        // One character, one tile: only the card that represents it appears.
        // Marking a card is information, not a way to add tiles — a character
        // whose versions were each given a tile ended up scattered across the
        // gallery by filename order, which is worse than not marking at all.
        // The one exception is a search: a note written on a hidden version is
        // useful only if searching for it can surface that version.
        if (!(card.IsLatestVersion
              || CardMetadataQuery.NoteMatches(card.VersionNote, terms))
            || !OriginPasses(card))
        {
            return false;
        }

        if (!CardMetadataQuery.MatchesText(card.MetadataSummary, card.FilePath, card.Author?.Name,
            card.MetadataLoaded ? card.CharacterName : null, terms, card.VersionNote)
            || !MetadataFilters.Matches(card.MetadataLoaded ? card.MetadataSummary : null)) return false;

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

        if (!hasSearch && !filterRes && !hasSourceFilter && !HasOriginFilter
            && MetadataFilters.ActiveCount == 0)
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
        if (_metadataStopping) return;
        _metadataScan.Queue(card, RefreshMetadataFilter, OnMetadataScanCompleted)
            .Observe(_logger, "CharacterGallery.ParseAddedCardMetadata");
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
        _metadataScan.Dispose();
        _metadataRefresh.Dispose();
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
        _metadataScan.Cancel(resetCount: true);
        _metadataRefresh.Stop();
    }
}

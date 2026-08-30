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
    private readonly CharacterMetadataService _metadataService;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly FriendService _friendService;
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
    private readonly Dictionary<string, List<CharacterCard>> _versionIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<CharacterCard> _lastCompletedCards = [];

    private CancellationTokenSource? _metadataCts;
    private const int MetadataParseConcurrency = 4;
    private DispatcherQueueTimer? _metadataRefreshTimer;

    public event Action<string?, string>? VersionIndexChanged;
    public event Action<string, string>? AlternativesChanged;

    public CharacterGalleryViewModel(
        CharacterCardService cardService,
        SettingsService settingsService,
        CharacterMetadataService metadataService,
        SettingsViewModel settingsViewModel,
        FriendService friendService,
        IAppLogger logger)
        : base(new ObservableCollection<CharacterCard>())
    {
        Cards = (ObservableCollection<CharacterCard>)_cardsSource;
        _cardService = cardService;
        _settingsService = settingsService;
        _metadataService = metadataService;
        _settingsViewModel = settingsViewModel;
        _friendService = friendService;
        _logger = logger;

        _cardService.CardAdded += OnCardAdded;
        _cardService.CardRemoved += OnCardRemoved;

        _settingsViewModel.ShowFileNamesChanged += OnShowFileNamesSettingChanged;
        _settingsViewModel.CharacterResolutionFilterChanged += OnCharacterResolutionFilterChanged;
        _settingsViewModel.CharacterSearchScopeChanged += OnCharacterSearchScopeChanged;
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
        if (HasCompletedLoad)
            _lastCompletedCards = [.. Cards];
        var cancellationToken = BeginLoad();
        var completed = false;
        _metadataCts?.Cancel();

        IsLoading = true;
        try
        {
            var config = await _settingsService.LoadConfigAsync();
            cancellationToken.ThrowIfCancellationRequested();
            ShowFileNames = config.ShowFileNames;
            _resolutionFilterEnabled = config.CharacterResolutionFilterEnabled;
            _allowedResolutions = [.. config.CharacterAllowedResolutions];

            var paths = FriendFolderLayout.CollapseNestedRoots(
                config.CharacterFolderPaths.Concat(
                    _friendService.GetCharacterFolders()));
            Cards.Clear();
            _cardIndex.Clear();
            _versionIndex.Clear();

            await _cardService.ScanFoldersAsync(
                paths,
                (batch, token) => EnqueueBatchAsync(
                    () =>
                    {
                        using (CardsView.DeferRefresh())
                        {
                            foreach (var card in batch)
                            {
                                if (!_cardIndex.TryAdd(card.FilePath, card)) continue;
                                Cards.Add(card);
                            }
                        }
                    },
                    "Unable to dispatch character card batch.",
                    token),
                cancellationToken);

            var linkedPaths = _friendService.GetLinkedCardPaths();
            var linkedCards = await Task.Run(() =>
            {
                var result = new List<CharacterCard>();
                foreach (var filePath in linkedPaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!File.Exists(filePath)
                        || _friendService.GetLinkedCardType(filePath)
                            != CardType.Character)
                    {
                        continue;
                    }

                    var card = _cardService.TryCreateFromPath(filePath);
                    if (card is not null)
                        result.Add(card);
                }
                return result;
            }, cancellationToken);
            foreach (var card in linkedCards)
            {
                if (_cardIndex.TryAdd(card.FilePath, card))
                    Cards.Add(card);
            }

            ApplyFilter();
            _cardService.StartWatching(paths);
            StartMetadataScan();
            completed = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally
        {
            if (_loadCts?.Token == cancellationToken)
            {
                if (completed)
                    _lastCompletedCards = [.. Cards];
                else
                    RestoreLastCompletedCards(
                        restartMetadata:
                            !cancellationToken.IsCancellationRequested);
                IsLoading = false;
                if (completed)
                    RaiseCardsReloaded();
            }
        }
    }

    private void RestoreLastCompletedCards(bool restartMetadata)
    {
        using (CardsView.DeferRefresh())
        {
            Cards.Clear();
            _cardIndex.Clear();
            _versionIndex.Clear();
            foreach (var card in _lastCompletedCards)
            {
                if (_cardIndex.TryAdd(card.FilePath, card))
                    Cards.Add(card);
            }
        }

        foreach (var card in Cards)
            UpdateVersionIndex(card);
        ApplyFilter();
        if (restartMetadata)
            StartMetadataScan();
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
                    if (!_cardIndex.TryGetValue(
                            card.FilePath,
                            out var current)
                        || !ReferenceEquals(current, card))
                    {
                        return;
                    }

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
        if (string.IsNullOrWhiteSpace(card.CharacterName))
            return;
        if (card.IsAlternative)
        {
            NotifyAlternativesChanged(card);
            return;
        }

        var key = GetVersionKey(card);
        if (!_versionIndex.TryGetValue(key, out var group))
        {
            group = [];
            _versionIndex[key] = group;
        }
        if (!group.Contains(card))
            group.Add(card);

        group.Sort((a, b) => CharacterVersionRules.CompareVersions(
            a.VariantKind,
            a.FileTimestamp,
            b.VariantKind,
            b.FileTimestamp));

        var count = group.Count;
        var latest = group
            .Where(item => item.VariantKind == CharacterVariantKind.Current)
            .MaxBy(item => item.FileTimestamp);
        for (int i = 0; i < count; i++)
        {
            group[i].VersionCount = count;
            group[i].IsLatestVersion = ReferenceEquals(group[i], latest);
            // The sort puts the newest current variant first, and the newest
            // old version first when the character has no current copy.
            group[i].IsVersionGroupRepresentative = i == 0;
        }

        VersionIndexChanged?.Invoke(
            card.FriendFolderPath,
            card.CharacterName);
    }

    private void RemoveFromVersionIndex(CharacterCard card)
    {
        if (string.IsNullOrWhiteSpace(card.CharacterName))
            return;
        if (card.IsAlternative)
        {
            NotifyAlternativesChanged(card);
            return;
        }
        var key = GetVersionKey(card);
        if (!_versionIndex.TryGetValue(key, out var group)) return;

        group.Remove(card);
        card.IsVersionGroupRepresentative = false;
        if (group.Count == 0)
        {
            _versionIndex.Remove(key);
            VersionIndexChanged?.Invoke(
                card.FriendFolderPath,
                card.CharacterName);
            return;
        }

        var count = group.Count;
        for (int i = 0; i < count; i++)
        {
            group[i].VersionCount = count;
            group[i].IsLatestVersion = group[i].VariantKind == CharacterVariantKind.Current
                && !group.Take(i).Any(item => item.VariantKind == CharacterVariantKind.Current);
            group[i].IsVersionGroupRepresentative = i == 0;
        }

        VersionIndexChanged?.Invoke(
            card.FriendFolderPath,
            card.CharacterName);
    }

    public List<CharacterCard>? GetVersions(CharacterCard card)
    {
        if (string.IsNullOrWhiteSpace(card.CharacterName)) return null;
        return _versionIndex.TryGetValue(GetVersionKey(card), out var group) && group.Count > 1
            ? group
            : null;
    }

    /// <summary>
    /// True while <paramref name="card"/> is still the live instance for its
    /// path. The index lookup keeps callers off a linear scan of the whole
    /// library — version-index notifications fire once per card during a
    /// metadata scan.
    /// </summary>
    public bool ContainsCard(CharacterCard card) =>
        _cardIndex.TryGetValue(card.FilePath, out var current)
        && ReferenceEquals(current, card);

    public List<CharacterCard> GetAlternatives(CharacterCard card)
    {
        if (card.FriendFolderPath is null
            || string.IsNullOrWhiteSpace(card.CharacterName))
            return [];

        return Cards
            .Where(candidate => !ReferenceEquals(candidate, card)
                && candidate.IsAlternative
                && candidate.FriendFolderPath is not null
                && candidate.FriendFolderPath.Equals(
                    card.FriendFolderPath,
                    StringComparison.OrdinalIgnoreCase)
                && candidate.CharacterName.Equals(
                    card.CharacterName,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.FileTimestamp)
            .ToList();
    }

    private static string GetVersionKey(CharacterCard card) =>
        FriendFolderLayout.BuildCharacterVersionKey(
            card.FriendFolderPath,
            card.CharacterName);

    private void NotifyAlternativesChanged(CharacterCard card)
    {
        if (card.FriendFolderPath is { } path)
            AlternativesChanged?.Invoke(path, card.CharacterName);
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

    private void OnCharacterSearchScopeChanged() =>
        _dispatcherQueue.TryEnqueue(ApplyFilter);

    public CharacterCard? GetRandomCard()
    {
        if (CardsView.Count == 0) return null;
        return CardsView[Random.Shared.Next(CardsView.Count)] as CharacterCard;
    }

    private bool BaseFilterPasses(CharacterCard card)
    {
        if (!IsVisibleForCurrentQuery(card)) return false;

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

    private bool IsVisibleForCurrentQuery(CharacterCard card) =>
        CharacterVersionRules.IsVisible(
            card.VariantKind,
            card.IsLatestVersion,
            card.IsVersionGroupRepresentative,
            hasSearchQuery: _searchKeywords.Length > 0,
            _settingsViewModel.IncludeAlternativeCharactersInSearch,
            _settingsViewModel.IncludeOldCharacterVersionsInSearch);

    protected override void ApplyFilter()
    {
        if (TryApplyShuffleFilter()) return;

        CardsView.Filter = item =>
            item is CharacterCard card && BaseFilterPasses(card);
        RefreshFilterAndNotify();
    }

    private void OnCardAdded(CharacterCard card)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_cardIndex.TryGetValue(card.FilePath, out var existing))
            {
                RemoveFromVersionIndex(existing);
                _cardIndex[card.FilePath] = card;
                var index = Cards.IndexOf(existing);
                if (index >= 0)
                    Cards[index] = card;
                else
                    Cards.Add(card);
            }
            else
            {
                _cardIndex.Add(card.FilePath, card);
                Cards.Add(card);
            }

            QueueMetadata(card);
            RaiseCardsChanged();
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
                RaiseCardsChanged();
            }
        });
    }

    public void Dispose()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _metadataCts?.Cancel();
        _metadataCts?.Dispose();
        _metadataRefreshTimer?.Stop();
        _cardService.CardAdded -= OnCardAdded;
        _cardService.CardRemoved -= OnCardRemoved;
        _settingsViewModel.ShowFileNamesChanged -= OnShowFileNamesSettingChanged;
        _settingsViewModel.CharacterResolutionFilterChanged -= OnCharacterResolutionFilterChanged;
        _settingsViewModel.CharacterSearchScopeChanged -= OnCharacterSearchScopeChanged;
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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public partial class ImportViewModel : ObservableObject
{
    private enum ManualAssignmentSource
    {
        Unknown,
        FetchFailed,
    }

    private sealed record ManualItemState(
        ImportItem Item,
        ArtworkId? ArtworkId,
        ContentRating Rating,
        string? AuthorName,
        string? AuthorId,
        string? Title,
        IReadOnlyList<ArtworkTag>? Tags,
        ImportItemStatus Status,
        string? ErrorMessage,
        string? ManualAuthorId,
        string? ManualArtworkId,
        string? DestinationPath);

    private sealed record ManualAssignmentUndo(
        ManualAssignmentSource Source,
        IReadOnlyList<ManualItemState> Items);

    private readonly ImportService _importService;
    private readonly DispatcherQueue _dispatcher;
    private CancellationTokenSource? _cts;

    // Flat collection used by ImportService (source of truth for all items)
    public ObservableCollection<ImportItem> Items { get; } = [];

    // Tree collections for the matched tab
    public ObservableCollection<ImportItem> AnalyzingItems { get; } = [];
    public ObservableCollection<ImportRatingGroup> MatchedGroups { get; } = [];

    // Items whose artwork ID was parsed but API fetch returned null (deleted/private),
    // grouped by artwork ID so multi-page posts stay together.
    public ObservableCollection<ImportArtworkGroup> FetchFailedGroups { get; } = [];

    // Grouped collection for the unknown tab (filename didn't match any provider pattern)
    public ObservableCollection<ImportUnknownGroup> UnknownGroups { get; } = [];

    // Authors available for manual assignment, split for grouped display
    public ObservableCollection<SelectableAuthor> BatchAuthors { get; } = [];
    public ObservableCollection<SelectableAuthor> LibraryAuthors { get; } = [];
    private bool _authorsLoaded;

    private readonly HashSet<ImportItem> _subscribedItems = [];
    private readonly HashSet<ImportArtworkGroup> _subscribedFetchFailedGroups = [];
    private readonly List<ImportItem> _pendingUnknownItems = [];
    private readonly HashSet<ImportUnknownGroup> _subscribedUnknownGroups = [];
    private readonly Dictionary<ImportItem, ManualItemState> _manualBaselines = [];
    private readonly HashSet<string> _currentAnalysisPaths = new(StringComparer.OrdinalIgnoreCase);
    private ManualAssignmentUndo? _lastManualAssignment;
    private int _currentRejectedAnalysisCount;
    private int _unknownGroupCounter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsAnalyzing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; set; }

    public bool IsEmpty => !HasItems;
    public bool IsIdle => !IsAnalyzing && !IsImporting;

    [ObservableProperty]
    public partial bool HasAnalyzingItems { get; set; }

    [ObservableProperty]
    public partial bool HasFetchFailedItems { get; set; }

    [ObservableProperty]
    public partial bool HasUnknownItems { get; set; }

    public int AnalysisPendingCount => AnalyzingItems.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalysisProgressPercent))]
    [NotifyPropertyChangedFor(nameof(AnalysisStatusText))]
    public partial int AnalysisTotalCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnalysisProgressPercent))]
    [NotifyPropertyChangedFor(nameof(AnalysisStatusText))]
    public partial int AnalysisCompletedCount { get; set; }

    public int AnalysisProgressPercent =>
        AnalysisTotalCount <= 0
            ? 0
            : (int)Math.Round((double)AnalysisCompletedCount / AnalysisTotalCount * 100);

    public string AnalysisStatusText =>
        AnalysisTotalCount <= 0
            ? string.Empty
            : $"{AnalysisCompletedCount}/{AnalysisTotalCount} ({AnalysisProgressPercent}%)";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUnknownItems))]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchAuthorIdToUnknownCommand))]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchArtworkIdToUnknownCommand))]
    public partial int SelectedUnknownCount { get; set; }

    public bool HasSelectedUnknownItems => SelectedUnknownCount > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedFetchFailedGroups))]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchAuthorIdToFetchFailedCommand))]
    public partial int SelectedFetchFailedCount { get; set; }

    public bool HasSelectedFetchFailedGroups => SelectedFetchFailedCount > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchAuthorIdToUnknownCommand))]
    public partial string? BatchManualAuthorId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchArtworkIdToUnknownCommand))]
    public partial string? BatchManualArtworkId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchAuthorIdToFetchFailedCommand))]
    public partial string? BatchFetchFailedAuthorId { get; set; }

    // Summary counts
    [ObservableProperty] public partial int R18GCount { get; set; }
    [ObservableProperty] public partial int R18Count { get; set; }
    [ObservableProperty] public partial int AllAgesCount { get; set; }
    [ObservableProperty] public partial int SceneCount { get; set; }
    [ObservableProperty] public partial int CharaCount { get; set; }
    [ObservableProperty] public partial int CoordCount { get; set; }
    [ObservableProperty] public partial int CompletedCount { get; set; }

    [ObservableProperty] public partial bool ShowRejectedWarning { get; set; }
    [ObservableProperty] public partial int RejectedCount { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoManualAssignmentCommand))]
    public partial bool CanUndoManualAssignment { get; set; }

    [ObservableProperty]
    public partial double ArtworkSubfolderThreshold { get; set; } = 1;

    [ObservableProperty]
    public partial bool UseVisualSimilarity { get; set; }

    private DispatcherTimer? _warningTimer;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _resolveCts;
    private Task _settingsLoaded;

    public ImportViewModel(ImportService importService, SettingsService settingsService, DispatcherQueue dispatcher)
    {
        _importService = importService;
        _settingsService = settingsService;
        _dispatcher = dispatcher;

        _settingsLoaded = LoadSettingsAsync();

        Items.CollectionChanged += OnItemsCollectionChanged;
        AnalyzingItems.CollectionChanged += OnAnalyzingItemsCollectionChanged;
        FetchFailedGroups.CollectionChanged += OnFetchFailedGroupsCollectionChanged;
        UnknownGroups.CollectionChanged += OnUnknownGroupsCollectionChanged;
    }

    private void OnAnalyzingItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasAnalyzingItems = AnalyzingItems.Count > 0;
        OnPropertyChanged(nameof(AnalysisPendingCount));
    }

    private void OnFetchFailedGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ImportArtworkGroup group in e.NewItems)
            {
                _subscribedFetchFailedGroups.Add(group);
                group.PropertyChanged += OnFetchFailedGroupPropertyChanged;
            }
        }
        else if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace
                 && e.OldItems is not null)
        {
            foreach (ImportArtworkGroup group in e.OldItems)
            {
                _subscribedFetchFailedGroups.Remove(group);
                group.PropertyChanged -= OnFetchFailedGroupPropertyChanged;
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var group in _subscribedFetchFailedGroups)
                group.PropertyChanged -= OnFetchFailedGroupPropertyChanged;
            _subscribedFetchFailedGroups.Clear();
        }

        HasFetchFailedItems = FetchFailedGroups.Count > 0;
        UpdateSelectedFetchFailedCount();
    }

    private void OnFetchFailedGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportArtworkGroup.IsSelected))
            UpdateSelectedFetchFailedCount();
    }

    private void UpdateSelectedFetchFailedCount() =>
        SelectedFetchFailedCount = FetchFailedGroups.Count(g => g.IsSelected);

    private void OnUnknownGroupsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ImportUnknownGroup group in e.NewItems)
            {
                _subscribedUnknownGroups.Add(group);
                group.PropertyChanged += OnUnknownGroupPropertyChanged;
            }
        }
        else if (e.Action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace
                 && e.OldItems is not null)
        {
            foreach (ImportUnknownGroup group in e.OldItems)
            {
                _subscribedUnknownGroups.Remove(group);
                group.PropertyChanged -= OnUnknownGroupPropertyChanged;
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var group in _subscribedUnknownGroups)
                group.PropertyChanged -= OnUnknownGroupPropertyChanged;
            _subscribedUnknownGroups.Clear();
        }

        HasUnknownItems = UnknownGroups.Count > 0;
        UpdateSelectedUnknownCount();
    }

    private void OnUnknownGroupPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportUnknownGroup.IsSelected))
            UpdateSelectedUnknownCount();
    }

    private void UpdateSelectedUnknownCount() =>
        SelectedUnknownCount = UnknownGroups.Count(g => g.IsSelected);

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ImportItem item in e.NewItems)
            {
                _subscribedItems.Add(item);
                item.PropertyChanged += OnItemPropertyChanged;
                PlaceNewItem(item);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var item in _subscribedItems)
                item.PropertyChanged -= OnItemPropertyChanged;
            _subscribedItems.Clear();
            AnalyzingItems.Clear();
            FetchFailedGroups.Clear();
            UnknownGroups.Clear();
            MatchedGroups.Clear();
            BatchAuthors.Clear();
            LibraryAuthors.Clear();
            foreach (var group in _subscribedFetchFailedGroups)
                group.PropertyChanged -= OnFetchFailedGroupPropertyChanged;
            _subscribedFetchFailedGroups.Clear();
            foreach (var group in _subscribedUnknownGroups)
                group.PropertyChanged -= OnUnknownGroupPropertyChanged;
            _subscribedUnknownGroups.Clear();
            _pendingUnknownItems.Clear();
            _unknownGroupCounter = 0;
            SelectedUnknownCount = 0;
            SelectedFetchFailedCount = 0;
            BatchManualAuthorId = null;
            BatchManualArtworkId = null;
            BatchFetchFailedAuthorId = null;
            _manualBaselines.Clear();
            _currentAnalysisPaths.Clear();
            _currentRejectedAnalysisCount = 0;
            _lastManualAssignment = null;
            CanUndoManualAssignment = false;
            _authorsLoaded = false;
        }
        UpdateCounts();
        UpdateAnalysisProgress();
    }

    private void PlaceNewItem(ImportItem item)
    {
        if (item.ArtworkId is null)
        {
            RememberManualBaseline(item);
            _pendingUnknownItems.Add(item);
            return;
        }
        AnalyzingItems.Add(item);
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ImportItem item) return;
        if (e.PropertyName == nameof(ImportItem.Rating))
        {
            UpdateCounts();
            return;
        }
        if (e.PropertyName != nameof(ImportItem.Status)) return;
        UpdateAnalysisProgress();
        if (item.Status != ImportItemStatus.ReadyToImport) return;

        // Phase 2 has finished for this item — move it to the right place in the tree
        if (!AnalyzingItems.Contains(item)) return;

        if (item.AuthorId is not null)
            PlaceItemInTree(item);
        else
            MoveFetchFailed(item);
    }

    private void PlaceItemInTree(ImportItem item)
    {
        AnalyzingItems.Remove(item);

        // Find or create the rating group (G < R18 < R18G order)
        var ratingGroup = MatchedGroups.FirstOrDefault(g => g.Rating == item.Rating);
        if (ratingGroup is null)
        {
            ratingGroup = new ImportRatingGroup(item.Rating);
            var insertAt = MatchedGroups.Count(g => (int)g.Rating < (int)item.Rating);
            MatchedGroups.Insert(insertAt, ratingGroup);
        }

        // Find or create the author group
        var authorGroup = ratingGroup.Authors.FirstOrDefault(a => a.AuthorId == item.AuthorId);
        if (authorGroup is null)
        {
            authorGroup = new ImportAuthorGroup(item.AuthorName!, item.AuthorId!);
            ratingGroup.Authors.Add(authorGroup);
            AddBatchAuthorIfLoaded(authorGroup.AuthorName, authorGroup.AuthorId);
        }

        // Find or create the artwork group
        var artworkId = item.ArtworkId?.Id ?? item.FileName;
        var artworkGroup = authorGroup.Artworks.FirstOrDefault(a => a.ArtworkId == artworkId);
        if (artworkGroup is null)
        {
            artworkGroup = new ImportArtworkGroup(item.Title, artworkId);
            authorGroup.Artworks.Add(artworkGroup);
        }

        artworkGroup.Files.Add(item);
    }

    private void MoveFetchFailed(ImportItem item)
    {
        AnalyzingItems.Remove(item);
        RememberManualBaseline(item);

        var artworkId = item.ArtworkId!.Id;
        var group = FetchFailedGroups.FirstOrDefault(g => g.ArtworkId == artworkId);
        if (group is null)
        {
            group = new ImportArtworkGroup(null, artworkId);
            var insertAt = FetchFailedGroups.Count(g =>
                string.Compare(g.ArtworkId, artworkId, StringComparison.Ordinal) < 0);
            FetchFailedGroups.Insert(insertAt, group);
        }
        group.Files.Add(item);

        if (!_authorsLoaded)
        {
            _authorsLoaded = true;
            _ = LoadAvailableAuthorsAsync();
        }
    }

    private async Task LoadAvailableAuthorsAsync()
    {
        try
        {
            var known = await _importService.GetKnownAuthorsAsync(CancellationToken.None);
            _dispatcher.TryEnqueue(() =>
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Batch authors first (from current import)
                foreach (var ratingGroup in MatchedGroups)
                    foreach (var authorGroup in ratingGroup.Authors)
                        if (seen.Add(authorGroup.AuthorId))
                            BatchAuthors.Add(new SelectableAuthor(authorGroup.AuthorName, authorGroup.AuthorId));

                // Library authors
                foreach (var (name, id) in known)
                {
                    if (seen.Add(id))
                        LibraryAuthors.Add(new SelectableAuthor(name, id));
                }
            });
        }
        catch { }
    }

    public string? ResolveAuthorName(string authorId)
    {
        var id = authorId.Trim();
        foreach (var a in BatchAuthors)
            if (a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return a.Name;
        foreach (var a in LibraryAuthors)
            if (a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return a.Name;
        return null;
    }

    public async Task<ReverseImageSearchResult?> SearchSauceNaoForFetchFailedGroupAsync(
        ImportArtworkGroup group,
        CancellationToken ct)
    {
        if (group.Files.Count == 0)
            return null;

        var apiKey = GetReverseImageSearchApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("SauceNao API key is not set.");

        return await _importService.SearchReverseImageAsync(
            group.Files[0].SourceFilePath,
            apiKey,
            ct);
    }

    private static string? GetReverseImageSearchApiKey()
    {
        if (App.PluginService.ReverseImageSearchProvider is not IPluginSettingsProvider settingsProvider)
            return null;

        return settingsProvider.GetSettingValue("sauceNaoApiKey")?.Trim();
    }

    public async Task ApplySauceNaoResultToFetchFailedGroupAsync(
        ImportArtworkGroup group,
        ReverseImageSearchResult result,
        ContentRating rating)
    {
        var files = group.Files.ToList();
        if (files.Count == 0)
            return;

        CaptureUndo(ManualAssignmentSource.FetchFailed, files);

        foreach (var item in files)
        {
            if (result.ArtworkId is not null)
                item.ArtworkId = result.ArtworkId;

            item.AuthorName = result.AuthorName;
            item.AuthorId = result.AuthorId;
            item.Title = result.Title;
            item.Rating = rating;
            item.Tags = [];
            item.ManualAuthorId = result.AuthorId;
            item.ErrorMessage = null;
            item.Status = ImportItemStatus.ReadyToImport;
        }

        FetchFailedGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        AddBatchAuthorIfLoaded(result.AuthorName, result.AuthorId);

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    private void AddBatchAuthorIfLoaded(string authorName, string authorId)
    {
        if (!_authorsLoaded) return;
        if (BatchAuthors.Any(a => a.Id.Equals(authorId, StringComparison.OrdinalIgnoreCase))) return;
        var libraryMatch = LibraryAuthors.FirstOrDefault(a => a.Id.Equals(authorId, StringComparison.OrdinalIgnoreCase));
        if (libraryMatch is not null)
            LibraryAuthors.Remove(libraryMatch);
        BatchAuthors.Add(new SelectableAuthor(authorName, authorId));
    }

    private void MoveToUnknown(ImportItem item)
    {
        RememberManualBaseline(item);
        var groupId = $"Group {++_unknownGroupCounter}";
        var group = new ImportUnknownGroup(groupId, [item]);
        UnknownGroups.Add(group);

        if (!_authorsLoaded)
        {
            _authorsLoaded = true;
            _ = LoadAvailableAuthorsAsync();
        }
    }

    private void FlushPendingUnknowns()
    {
        if (_pendingUnknownItems.Count == 0) return;

        var items = _pendingUnknownItems.ToList();
        _pendingUnknownItems.Clear();

        // Include existing unknown items so new items can merge into existing groups
        var allUnknownItems = new List<ImportItem>();
        foreach (var existing in UnknownGroups)
            foreach (var file in existing.Files)
                allUnknownItems.Add(file);
        allUnknownItems.AddRange(items);

        // Remove old groups and rebuild from the combined set
        UnknownGroups.Clear();

        var groups = CardGroupingService.GroupByVisualSimilarity(allUnknownItems);
        foreach (var groupItems in groups)
        {
            var groupId = $"Group {++_unknownGroupCounter}";
            var group = new ImportUnknownGroup(groupId, groupItems);
            UnknownGroups.Add(group);
        }

        if (!_authorsLoaded && UnknownGroups.Count > 0)
        {
            _authorsLoaded = true;
            _ = LoadAvailableAuthorsAsync();
        }
    }

    [RelayCommand]
    private async Task AssignAuthorToUnknownGroupAsync(ImportUnknownGroup group)
    {
        if (!group.CanAssignAuthor) return;

        var files = group.Files.ToList();
        CaptureUndo(ManualAssignmentSource.Unknown, files);

        var id = group.ManualAuthorId.Trim();
        var name = ResolveAuthorName(id) ?? id;

        foreach (var item in files)
        {
            item.ManualAuthorId = id;
            item.AuthorName = name;
            item.AuthorId = id;
        }

        UnknownGroups.Remove(group);
        foreach (var item in files)
            PlaceItemInTree(item);

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    [RelayCommand]
    private async Task AssignArtworkIdToUnknownGroupAsync(ImportUnknownGroup group)
    {
        if (!group.CanAssignArtworkId) return;

        var files = group.Files.ToList();
        CaptureUndo(ManualAssignmentSource.Unknown, files);

        UnknownGroups.Remove(group);

        var artworkId = _importService.CreateManualArtworkId(group.ManualArtworkId);

        foreach (var item in files)
        {
            item.ArtworkId = artworkId;
            item.AuthorName = null;
            item.AuthorId = null;
            item.Title = null;
            item.Tags = null;
            item.ErrorMessage = null;
            item.Status = ImportItemStatus.Analyzing;
            AnalyzingItems.Add(item);
        }

        var info = await _importService.FetchArtworkInfoAsync(artworkId, CancellationToken.None);
        foreach (var item in files)
        {
            if (info is not null)
            {
                item.AuthorName = info.AuthorName;
                item.AuthorId = info.AuthorId;
                item.Title = info.Title;
                item.Rating = info.Rating;
                item.Tags = info.Tags;
            }
            item.Status = ImportItemStatus.ReadyToImport;
        }

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    private bool CanAssignBatchAuthorIdToUnknown() =>
        SelectedUnknownCount > 0 && !string.IsNullOrWhiteSpace(BatchManualAuthorId);

    [RelayCommand(CanExecute = nameof(CanAssignBatchAuthorIdToUnknown))]
    private async Task AssignBatchAuthorIdToUnknownAsync()
    {
        var groups = UnknownGroups.Where(g => g.IsSelected).ToList();
        if (groups.Count == 0 || string.IsNullOrWhiteSpace(BatchManualAuthorId))
            return;

        var files = groups.SelectMany(g => g.Files).ToList();
        CaptureUndo(ManualAssignmentSource.Unknown, files);

        var id = BatchManualAuthorId.Trim();
        var name = ResolveAuthorName(id) ?? id;

        foreach (var item in files)
        {
            item.ManualAuthorId = id;
            item.AuthorName = name;
            item.AuthorId = id;
        }

        foreach (var group in groups)
            UnknownGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        BatchManualAuthorId = null;

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    private bool CanAssignBatchArtworkIdToUnknown() =>
        SelectedUnknownCount > 0 && !string.IsNullOrWhiteSpace(BatchManualArtworkId);

    [RelayCommand(CanExecute = nameof(CanAssignBatchArtworkIdToUnknown))]
    private async Task AssignBatchArtworkIdToUnknownAsync()
    {
        var groups = UnknownGroups.Where(g => g.IsSelected).ToList();
        if (groups.Count == 0 || string.IsNullOrWhiteSpace(BatchManualArtworkId))
            return;

        var files = groups.SelectMany(g => g.Files).ToList();
        CaptureUndo(ManualAssignmentSource.Unknown, files);

        var artworkId = _importService.CreateManualArtworkId(BatchManualArtworkId);

        foreach (var group in groups)
            UnknownGroups.Remove(group);

        foreach (var item in files)
        {
            item.ArtworkId = artworkId;
            item.AuthorName = null;
            item.AuthorId = null;
            item.Title = null;
            item.Tags = null;
            item.ErrorMessage = null;
            item.Status = ImportItemStatus.Analyzing;
            AnalyzingItems.Add(item);
        }

        var info = await _importService.FetchArtworkInfoAsync(artworkId, CancellationToken.None);
        foreach (var item in files)
        {
            if (info is not null)
            {
                item.AuthorName = info.AuthorName;
                item.AuthorId = info.AuthorId;
                item.Title = info.Title;
                item.Rating = info.Rating;
                item.Tags = info.Tags;
            }
            item.Status = ImportItemStatus.ReadyToImport;
        }

        BatchManualArtworkId = null;

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    [RelayCommand]
    private void RemoveUnknownGroup(ImportUnknownGroup group)
    {
        var files = group.Files.ToList();
        UnknownGroups.Remove(group);
        foreach (var item in files)
            Items.Remove(item);
        UpdateCounts();
    }

    [RelayCommand]
    private void RemoveUnknownItem(ImportItem item)
    {
        foreach (var group in UnknownGroups.ToList())
        {
            if (!group.Files.Remove(item)) continue;
            if (group.Files.Count == 0)
                UnknownGroups.Remove(group);
            break;
        }
        Items.Remove(item);
        UpdateCounts();
    }

    [RelayCommand]
    private async Task AddFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        var newPaths = filePaths
            .Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Where(p => Items.All(existing => !string.Equals(existing.SourceFilePath, p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (newPaths.Count == 0) return;

        await _settingsLoaded;
        BeginAnalysisProgress(newPaths);
        IsAnalyzing = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            int rejected = await _importService.AnalyzeAsync(newPaths, Items, _dispatcher, _cts.Token);
            _currentRejectedAnalysisCount = rejected;
            UpdateAnalysisProgress();

            // Compute fingerprints for all new items (used for subfolder decisions and unknown grouping)
            var newPathSet = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
            var newItems = Items.Where(i => newPathSet.Contains(i.SourceFilePath)).ToList();
            await _importService.ComputeFingerprintsAsync(newItems, _cts.Token);

            // Re-resolve destinations now that fingerprints are available for subfolder decisions
            await ReResolveDestinationsAsync(_cts.Token);

            // Flush buffered unknowns into groups (fingerprints already computed above)
            FlushPendingUnknowns();

            if (rejected > 0)
                ShowRejectedFiles(rejected);
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsAnalyzing = false;
            EndAnalysisProgress();
            UpdateCounts();
        }
    }

    [RelayCommand]
    private async Task ImportAllAsync()
    {
        if (IsImporting) return;
        IsImporting = true;
        _cts = new CancellationTokenSource();
        bool completed = false;

        try
        {
            await _importService.ImportAsync(Items, _dispatcher, _cts.Token);
            completed = true;
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsImporting = false;
            UpdateCounts();
        }

        if (completed)
            Clear();
    }

    [RelayCommand]
    private async Task AssignAuthorAsync(ImportArtworkGroup group)
    {
        if (!group.CanAssignAuthor) return;

        var files = group.Files.ToList();
        CaptureUndo(ManualAssignmentSource.FetchFailed, files);

        var authorId = group.ManualAuthorId.Trim();
        var authorName = ResolveAuthorName(authorId) ?? authorId;

        foreach (var item in files)
        {
            item.AuthorName = authorName;
            item.AuthorId = authorId;
        }

        FetchFailedGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    private bool CanAssignBatchAuthorIdToFetchFailed() =>
        SelectedFetchFailedCount > 0 && !string.IsNullOrWhiteSpace(BatchFetchFailedAuthorId);

    [RelayCommand(CanExecute = nameof(CanAssignBatchAuthorIdToFetchFailed))]
    private async Task AssignBatchAuthorIdToFetchFailedAsync()
    {
        var groups = FetchFailedGroups.Where(g => g.IsSelected).ToList();
        if (groups.Count == 0 || string.IsNullOrWhiteSpace(BatchFetchFailedAuthorId))
            return;

        var files = groups.SelectMany(g => g.Files).ToList();
        if (files.Count == 0)
            return;

        CaptureUndo(ManualAssignmentSource.FetchFailed, files);

        var authorId = BatchFetchFailedAuthorId.Trim();
        var authorName = ResolveAuthorName(authorId) ?? authorId;

        foreach (var item in files)
        {
            item.ManualAuthorId = authorId;
            item.AuthorName = authorName;
            item.AuthorId = authorId;
        }

        foreach (var group in groups)
            FetchFailedGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        BatchFetchFailedAuthorId = null;
        UpdateSelectedFetchFailedCount();

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    [RelayCommand(CanExecute = nameof(CanUndoManualAssignment))]
    private async Task UndoManualAssignmentAsync()
    {
        if (_lastManualAssignment is null) return;

        var undo = _lastManualAssignment;
        _lastManualAssignment = null;
        CanUndoManualAssignment = false;

        var unknownItems = new List<ImportItem>();
        foreach (var state in undo.Items)
        {
            RemoveItemFromManualContainers(state.Item);
            RestoreManualState(state);

            if (undo.Source == ManualAssignmentSource.Unknown)
                unknownItems.Add(state.Item);
            else
                MoveFetchFailed(state.Item);
        }

        if (unknownItems.Count > 0)
        {
            var groups = CardGroupingService.GroupByVisualSimilarity(unknownItems);
            foreach (var groupItems in groups)
            {
                var groupId = $"Group {++_unknownGroupCounter}";
                var group = new ImportUnknownGroup(groupId, groupItems);
                UnknownGroups.Add(group);
            }

            if (!_authorsLoaded && UnknownGroups.Count > 0)
            {
                _authorsLoaded = true;
                _ = LoadAvailableAuthorsAsync();
            }
        }

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    [RelayCommand]
    private void Clear()
    {
        _cts?.Cancel();
        _lastManualAssignment = null;
        CanUndoManualAssignment = false;
        Items.Clear();
        HasItems = false;
        UpdateCounts();
    }

    private void ShowRejectedFiles(int count)
    {
        RejectedCount = count;
        ShowRejectedWarning = true;

        _warningTimer?.Stop();
        _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _warningTimer.Tick += (_, _) =>
        {
            _warningTimer.Stop();
            ShowRejectedWarning = false;
        };
        _warningTimer.Start();
    }

    private async Task LoadSettingsAsync()
    {
        var config = await _settingsService.LoadConfigAsync();
        ArtworkSubfolderThreshold = config.ArtworkSubfolderThreshold;
        UseVisualSimilarity = config.UseVisualSimilarity;
    }

    partial void OnArtworkSubfolderThresholdChanged(double value)
    {
        if (HasItems)
            _ = DebouncedReResolveAsync();
    }

    partial void OnUseVisualSimilarityChanged(bool value)
    {
        if (HasItems)
            _ = DebouncedReResolveAsync();
    }

    private async Task DebouncedReResolveAsync()
    {
        _resolveCts?.Cancel();
        var cts = _resolveCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(150, cts.Token);
            await ReResolveDestinationsAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
    }

    private Task ReResolveDestinationsAsync(CancellationToken ct = default) =>
        _importService.ReResolveAsync(Items, _dispatcher, ct,
            (int)ArtworkSubfolderThreshold, UseVisualSimilarity);

    private void BeginAnalysisProgress(IReadOnlyList<string> filePaths)
    {
        _currentAnalysisPaths.Clear();
        foreach (var path in filePaths)
            _currentAnalysisPaths.Add(path);

        _currentRejectedAnalysisCount = 0;
        AnalysisTotalCount = filePaths.Count;
        AnalysisCompletedCount = 0;
    }

    private void EndAnalysisProgress()
    {
        _currentAnalysisPaths.Clear();
        _currentRejectedAnalysisCount = 0;
        AnalysisTotalCount = 0;
        AnalysisCompletedCount = 0;
    }

    private void UpdateAnalysisProgress()
    {
        if (AnalysisTotalCount <= 0 || _currentAnalysisPaths.Count == 0)
            return;

        var completedItems = Items.Count(i =>
            _currentAnalysisPaths.Contains(i.SourceFilePath) &&
            i.Status != ImportItemStatus.Analyzing);

        AnalysisCompletedCount = Math.Clamp(
            completedItems + _currentRejectedAnalysisCount,
            0,
            AnalysisTotalCount);
    }

    private void UpdateCounts()
    {
        HasItems = Items.Count > 0;
        var ready = Items.Where(i => i.Status is ImportItemStatus.ReadyToImport or ImportItemStatus.Completed).ToList();
        R18GCount = ready.Count(i => i.Rating == ContentRating.R18G);
        R18Count = ready.Count(i => i.Rating == ContentRating.R18);
        AllAgesCount = ready.Count(i => i.Rating == ContentRating.AllAges);
        SceneCount = Items.Count(i => i.CardType == CardType.Scene);
        CharaCount = Items.Count(i => i.CardType == CardType.Character);
        CoordCount = Items.Count(i => i.CardType == CardType.Coordinate);
        CompletedCount = Items.Count(i => i.Status == ImportItemStatus.Completed);
    }

    private void RememberManualBaseline(ImportItem item)
    {
        _manualBaselines.TryAdd(item, CreateManualState(item));
    }

    private void CaptureUndo(ManualAssignmentSource source, IReadOnlyList<ImportItem> items)
    {
        _lastManualAssignment = new ManualAssignmentUndo(
            source,
            items.Select(item => _manualBaselines.GetValueOrDefault(item, CreateManualState(item))).ToList());
        CanUndoManualAssignment = true;
    }

    private static ManualItemState CreateManualState(ImportItem item) =>
        new(
            item,
            item.ArtworkId,
            item.Rating,
            item.AuthorName,
            item.AuthorId,
            item.Title,
            item.Tags,
            item.Status,
            item.ErrorMessage,
            item.ManualAuthorId,
            item.ManualArtworkId,
            item.DestinationPath);

    private static void RestoreManualState(ManualItemState state)
    {
        state.Item.ArtworkId = state.ArtworkId;
        state.Item.Rating = state.Rating;
        state.Item.AuthorName = state.AuthorName;
        state.Item.AuthorId = state.AuthorId;
        state.Item.Title = state.Title;
        state.Item.Tags = state.Tags;
        state.Item.Status = state.Status;
        state.Item.ErrorMessage = state.ErrorMessage;
        state.Item.ManualAuthorId = state.ManualAuthorId;
        state.Item.ManualArtworkId = state.ManualArtworkId;
        state.Item.DestinationPath = state.DestinationPath;
    }

    private void RemoveItemFromManualContainers(ImportItem item)
    {
        AnalyzingItems.Remove(item);
        RemoveItemFromUnknownGroups(item);
        RemoveItemFromFetchFailed(item);
        RemoveItemFromTree(item);
    }

    private void RemoveItemFromUnknownGroups(ImportItem item)
    {
        foreach (var group in UnknownGroups.ToList())
        {
            if (!group.Files.Remove(item)) continue;
            if (group.Files.Count == 0)
                UnknownGroups.Remove(group);
            return;
        }
    }

    private void RemoveItemFromFetchFailed(ImportItem item)
    {
        foreach (var group in FetchFailedGroups.ToList())
        {
            if (!group.Files.Remove(item)) continue;
            if (group.Files.Count == 0)
                FetchFailedGroups.Remove(group);
            return;
        }
    }

    private void RemoveItemFromTree(ImportItem item)
    {
        foreach (var ratingGroup in MatchedGroups.ToList())
        {
            foreach (var authorGroup in ratingGroup.Authors.ToList())
            {
                foreach (var artworkGroup in authorGroup.Artworks.ToList())
                {
                    if (!artworkGroup.Files.Remove(item)) continue;
                    if (artworkGroup.Files.Count == 0)
                        authorGroup.Artworks.Remove(artworkGroup);
                    if (authorGroup.Artworks.Count == 0)
                        ratingGroup.Authors.Remove(authorGroup);
                    if (ratingGroup.Authors.Count == 0)
                        MatchedGroups.Remove(ratingGroup);
                    return;
                }
            }
        }
    }
}

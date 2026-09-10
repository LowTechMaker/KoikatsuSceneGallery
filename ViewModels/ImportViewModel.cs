using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.WinUI.Collections;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public partial class ImportViewModel : ObservableObject
{
    private static readonly ResourceLoader ResLoader = new();

    private readonly ImportService _importService;
    private readonly ImportArtworkAssignment _artworkAssignment;
    private readonly ImportReviewEditService _reviewEditService;
    private readonly ImportExecutionCoordinator _executionCoordinator;
    private readonly PluginService _pluginService;
    private readonly DispatcherQueue _dispatcher;
    private readonly ImportAnalysisTracker _analysisTracker = new();

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

    // Flattened, UI-only projection of all workspace items. This deliberately does
    // not replace the legacy groups used by the manual-assignment workflows.
    public ObservableCollection<ImportItemReviewState> ReviewStates => _reviewWorkspace.States;
    public AdvancedCollectionView ReviewedItemsView { get; }
    public ObservableCollection<string> ReviewAuthorProviderIds { get; } = [];
    public ObservableCollection<string> ReviewArtworkProviderIds { get; } = [];

    // Authors available for manual assignment, split for grouped display
    public ObservableCollection<SelectableAuthor> BatchAuthors { get; } = [];
    public ObservableCollection<SelectableAuthor> LibraryAuthors { get; } = [];
    private bool _authorsLoaded;

    private readonly CollectionItemObserver<ImportItem> _itemsObserver;
    private readonly CollectionItemObserver<ImportArtworkGroup> _fetchFailedObserver;
    private readonly List<ImportItem> _pendingUnknownItems = [];
    private readonly CollectionItemObserver<ImportUnknownGroup> _unknownObserver;
    private readonly ImportManualAssignmentHistory _manualHistory = new();
    private int _unknownGroupCounter;
    private readonly ImportReviewWorkspace _reviewWorkspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanConfirmImport))]
    [NotifyCanExecuteChangedFor(nameof(RefreshLibraryIndexCommand), nameof(ConfirmImportCommand))]
    public partial bool IsAnalyzing { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(CanConfirmImport))]
    [NotifyPropertyChangedFor(nameof(IsReviewWorkspaceEnabled))]
    [NotifyCanExecuteChangedFor(nameof(RefreshLibraryIndexCommand), nameof(ConfirmImportCommand))]
    public partial bool IsImporting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TransactionProgressCountsText))]
    internal partial ImportExecutionProgressSnapshot? ExecutionProgress { get; set; }

    [ObservableProperty]
    public partial string TransactionStatusText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; set; }

    public bool IsEmpty => !HasItems;
    public bool IsIdle => !IsAnalyzing && !IsImporting;
    public bool IsReviewWorkspaceEnabled => IsIdle && !IsResolvingReview;
    [ObservableProperty]
    public partial bool IsResolvingReview { get; set; }
    partial void OnIsResolvingReviewChanged(bool value) => NotifyReviewWorkspaceState();
    public int ImportableCount => Items.Count(item => ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath));
    public bool CanConfirmImport => IsReviewWorkspaceEnabled && ImportableCount > 0;
    public Visibility AnalysisLoadingVisibility => IsAnalyzing ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FlatReviewWorkspaceVisibility => ReviewStates.Count > 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility EmptyReviewWorkspaceVisibility => !IsAnalyzing && ReviewStates.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility FilteredReviewEmptyVisibility => !IsAnalyzing && ReviewStates.Count > 0 && ReviewedItemsView.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility UnknownEmptyVisibility => !IsAnalyzing && !HasUnknownItems
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility UnknownLoadingVisibility => IsAnalyzing && !HasUnknownItems
        ? Visibility.Visible
        : Visibility.Collapsed;
    public int ReviewFilterIndex
    {
        get => SelectedReviewFilter switch { "AllAges" => 1, "R18" => 2, "R18G" => 3, _ => 0 };
        set => SelectedReviewFilter = value switch { 1 => "AllAges", 2 => "R18", 3 => "R18G", _ => "All" };
    }
    public ObservableCollection<ImportReviewRow> ReviewRows => _reviewWorkspace.Rows;
    public string UnidentifiedNavigationText => ReviewNavigationText("Unidentified", "Import_Mixed_JumpUnidentified");
    public string UnavailableNavigationText => ReviewNavigationText("Unavailable", "Import_Mixed_JumpUnavailable");
    public string IdentifiedNavigationText => ReviewNavigationText("Identified", "Import_Mixed_JumpIdentified");
    private string ReviewNavigationText(string section, string resource)
        => string.Format(GetLocalizedString(resource), ReviewRows.FirstOrDefault(r => r.Key == section + ":")?.Items.Count ?? 0);
    public IReadOnlyList<ImportItem> SelectedVisibleReviewItems => _reviewWorkspace.SelectedItems;

    public string ReviewSummaryText => string.Format(GetLocalizedString("Import_Review_Summary"), Items.Count, ImportableCount);
    public string ReviewSelectionText => string.Format(GetLocalizedString("Import_Review_SelectionCount"), ReviewedItemsView.Count, SelectedReviewCount);
    public string ImportActionText => string.Format(GetLocalizedString("Import_ActionCount"), ImportableCount);
    public Visibility ReviewSelectionEditorVisibility => HasSelectedReviewItems
        ? Visibility.Visible
        : Visibility.Collapsed;
    public Visibility ReviewSelectionPromptVisibility => HasSelectedReviewItems
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string ReviewEmptyTitle => ReadyWithoutDestinationCount > 0
        ? GetLocalizedString("Import_Review_NoDestination_Title")
        : GetLocalizedString(Items.Count > 0 && Items.All(item => item.IsAlreadyInLibrary)
            ? "Import_Review_AlreadyInLibrary_Title" : "Import_ReviewEmpty_Title");
    public string ReviewEmptyDescription => ReadyWithoutDestinationCount > 0
        ? string.Format(GetLocalizedString("Import_Review_NoDestination_Description"), ReadyWithoutDestinationCount)
        : GetLocalizedString(Items.Count > 0 && Items.All(item => item.IsAlreadyInLibrary)
            ? "Import_Review_AlreadyInLibrary_Description" : "Import_ReviewEmpty_Description");

    public int SelectedReviewCount => _reviewWorkspace.SelectedCount;
    public bool HasSelectedReviewItems => SelectedReviewCount > 0;
    public bool? IsAllReviewSelected
    {
        get => _reviewWorkspace.AllSelected;
        set { if (_reviewWorkspace.AllSelected != value) _reviewWorkspace.SelectAll(value); }
    }
    public string SelectedReviewFilter
    {
        get => _reviewWorkspace.Filter;
        set { if (_reviewWorkspace.Filter != value) _reviewWorkspace.SetFilter(value); }
    }

    [ObservableProperty]
    public partial string? ReviewAuthorProviderId { get; set; }

    [ObservableProperty]
    public partial string? ReviewAuthorId { get; set; }

    [ObservableProperty]
    public partial string? ReviewArtworkProviderId { get; set; }

    [ObservableProperty]
    public partial string? ReviewArtworkId { get; set; }

    [ObservableProperty]
    public partial int ReviewRatingOverrideIndex { get; set; }

    [ObservableProperty]
    public partial string ReviewBatchStatusText { get; set; } = string.Empty;
    public Visibility ReviewBatchStatusVisibility => string.IsNullOrWhiteSpace(ReviewBatchStatusText) ? Visibility.Collapsed : Visibility.Visible;
    partial void OnReviewBatchStatusTextChanged(string value) => OnPropertyChanged(nameof(ReviewBatchStatusVisibility));
    public Visibility ClearReviewRatingFilterVisibility => SelectedReviewFilter == "All" ? Visibility.Collapsed : Visibility.Visible;
    public string ReviewFilterEmptyText => GetLocalizedString("Import_Review_FilterEmpty.Text");

    public string TransactionProgressCountsText => ExecutionProgress is null
        ? string.Empty
        : string.Format(
            GetLocalizedString("Import_Progress_Counters"),
            ExecutionProgress.SuccessCount,
            ExecutionProgress.FailedCount,
            ExecutionProgress.WarningCount);

    [ObservableProperty]
    public partial bool HasAnalyzingItems { get; set; }

    [ObservableProperty]
    public partial bool HasFetchFailedItems { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnknownEmptyVisibility), nameof(UnknownLoadingVisibility))]
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

    public string? BatchManualAuthorProviderId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchArtworkIdToUnknownCommand))]
    public partial string? BatchManualArtworkId { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignBatchAuthorIdToFetchFailedCommand))]
    public partial string? BatchFetchFailedAuthorId { get; set; }

    public string? BatchFetchFailedAuthorProviderId { get; set; }

    // Summary counts
    [ObservableProperty] public partial int R18GCount { get; set; }
    [ObservableProperty] public partial int R18Count { get; set; }
    [ObservableProperty] public partial int AllAgesCount { get; set; }
    [ObservableProperty] public partial int SceneCount { get; set; }
    [ObservableProperty] public partial int CharaCount { get; set; }
    [ObservableProperty] public partial int CoordCount { get; set; }
    [ObservableProperty] public partial int CompletedCount { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReviewEmptyTitle), nameof(ReviewEmptyDescription))]
    public partial int ReadyWithoutDestinationCount { get; set; }

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
    private readonly IAppLogger _logger;
    private Task _settingsLoaded;

    internal ImportViewModel(ImportService importService, ImportExecutionCoordinator executionCoordinator, SettingsService settingsService, PluginService pluginService, DispatcherQueue dispatcher, IAppLogger logger)
    {
        _importService = importService;
        _artworkAssignment = new ImportArtworkAssignment(importService.FetchArtworkInfoAsync);
        _reviewEditService = new ImportReviewEditService(importService.ResolveReviewArtworkInput,
            importService.FetchArtworkInfoAsync, (id, provider) => ResolveAuthor(id, provider).Name);
        _executionCoordinator = executionCoordinator;
        _settingsService = settingsService;
        _pluginService = pluginService;
        _dispatcher = dispatcher;
        _logger = logger;

        _reviewWorkspace = new ImportReviewWorkspace(GetLocalizedString,
            action => _dispatcher.TryEnqueue(() => action()));
        ReviewedItemsView = new AdvancedCollectionView(_reviewWorkspace.VisibleItems, false);
        _reviewWorkspace.Changed += OnReviewWorkspaceChanged;
        _reviewWorkspace.RowsChanged += OnReviewRowsChanged;
        foreach (var id in _pluginService.AuthorProviders.Select(p => p.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase))
            ReviewAuthorProviderIds.Add(id);
        foreach (var id in _pluginService.ImportProviders.Select(p => p.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase))
            ReviewArtworkProviderIds.Add(id);

        _settingsLoaded = LoadSettingsAsync();

        _itemsObserver = new(Items, OnItemPropertyChanged, e => OnItemsCollectionChanged(Items, e));
        AnalyzingItems.CollectionChanged += OnAnalyzingItemsCollectionChanged;
        _fetchFailedObserver = new(FetchFailedGroups, OnFetchFailedGroupPropertyChanged, OnFetchFailedGroupsChanged);
        _unknownObserver = new(UnknownGroups, OnUnknownGroupPropertyChanged, OnUnknownGroupsChanged);
    }

    private void OnAnalyzingItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        HasAnalyzingItems = AnalyzingItems.Count > 0;
        OnPropertyChanged(nameof(AnalysisPendingCount));
    }

    private void OnFetchFailedGroupsChanged()
    {
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

    private void OnUnknownGroupsChanged()
    {
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

    [RelayCommand]
    private void ToggleAllUnknownSelection()
    {
        bool shouldSelect = SelectedUnknownCount < UnknownGroups.Count;
        foreach (var group in UnknownGroups)
            group.IsSelected = shouldSelect;

        UpdateSelectedUnknownCount();
    }

    [RelayCommand]
    private void ToggleAllFetchFailedSelection()
    {
        bool shouldSelect = SelectedFetchFailedCount < FetchFailedGroups.Count;
        foreach (var group in FetchFailedGroups)
            group.IsSelected = shouldSelect;

        UpdateSelectedFetchFailedCount();
    }

    [RelayCommand]
    private void SetBatchRatingForFetchFailed(ContentRating rating)
    {
        foreach (var group in FetchFailedGroups.Where(g => g.IsSelected))
            foreach (var item in group.Files)
                item.Rating = rating;

        UpdateCounts();
    }

    [RelayCommand]
    private void SetBatchRatingForUnknown(ContentRating rating)
    {
        foreach (var group in UnknownGroups.Where(g => g.IsSelected))
            foreach (var item in group.Files)
                item.Rating = rating;

        UpdateCounts();
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            foreach (ImportItem item in e.NewItems)
            {
                PlaceNewItem(item);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            AnalyzingItems.Clear();
            FetchFailedGroups.Clear();
            UnknownGroups.Clear();
            MatchedGroups.Clear();
            BatchAuthors.Clear();
            LibraryAuthors.Clear();
            _pendingUnknownItems.Clear();
            _unknownGroupCounter = 0;
            SelectedUnknownCount = 0;
            SelectedFetchFailedCount = 0;
            BatchManualAuthorId = null;
            BatchManualArtworkId = null;
            BatchFetchFailedAuthorId = null;
            BatchManualAuthorProviderId = null;
            BatchFetchFailedAuthorProviderId = null;
            _manualHistory.Clear();
            _analysisTracker.ResetProgress();
            AnalysisTotalCount = 0;
            AnalysisCompletedCount = 0;
            CanUndoManualAssignment = false;
            _authorsLoaded = false;
            ClearReviewStates();
        }
        UpdateCounts();
        UpdateAnalysisProgress();
        ReconcileReviewStatesIfVisible();
    }

    private void PlaceNewItem(ImportItem item)
    {
        if (item.ArtworkId is null)
        {
            _manualHistory.RememberBaseline(item);
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
            ReconcileReviewStatesIfVisible();
            return;
        }
        if (e.PropertyName != nameof(ImportItem.Status))
        {
            if (e.PropertyName is nameof(ImportItem.DestinationPath) or nameof(ImportItem.ArtworkId))
            {
                UpdateCounts();
                ReconcileReviewStatesIfVisible();
            }
            return;
        }
        UpdateCounts();
        UpdateAnalysisProgress();
        if (item.Status != ImportItemStatus.ReadyToImport)
        {
            ReconcileReviewStatesIfVisible();
            return;
        }

        // Phase 2 completes through a dispatcher callback.  The preceding
        // destination pass can therefore have observed this item while it was
        // still Analyzing and skipped it.  Coalesce one final pass after Ready
        // transitions so every accepted card receives a destination.
        if (AnalyzingItems.Contains(item))
        {
            if (item.AuthorId is not null)
                PlaceItemInTree(item);
            else
                MoveFetchFailed(item);
        }

        DebouncedReResolveAsync().Observe(_logger, "Import.ReadyStateResolve");
        ReconcileReviewStatesIfVisible();
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
        var providerId = item.ArtworkId?.ProviderId ?? item.AuthorProviderId;
        var authorGroup = ratingGroup.Authors.FirstOrDefault(a =>
            a.AuthorId == item.AuthorId
            && string.Equals(a.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));
        if (authorGroup is null)
        {
            authorGroup = new ImportAuthorGroup(item.AuthorName!, item.AuthorId!, providerId);
            ratingGroup.Authors.Add(authorGroup);
            AddBatchAuthorIfLoaded(authorGroup.AuthorName, authorGroup.AuthorId, authorGroup.ProviderId);
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
        _manualHistory.RememberBaseline(item);

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
            LoadAvailableAuthorsAsync().Observe(_logger, "Import.LoadAvailableAuthors");
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
                        if (seen.Add(AuthorKey(authorGroup.ProviderId, authorGroup.AuthorId)))
                            BatchAuthors.Add(new SelectableAuthor(authorGroup.AuthorName, authorGroup.AuthorId, authorGroup.ProviderId));

                // Library authors
                foreach (var (name, id, providerId) in known)
                {
                    if (seen.Add(AuthorKey(providerId, id)))
                        LibraryAuthors.Add(new SelectableAuthor(name, id, providerId));
                }
            });
        }
        catch (Exception ex) { _logger.LogError("Import.LoadAvailableAuthors", ex); }
    }

    public string? ResolveAuthorName(string authorId)
        => ResolveAuthor(authorId).Name;

    private (string Name, string? ProviderId) ResolveAuthor(string authorId, string? preferredProviderId = null)
    {
        var id = authorId.Trim();
        var matches = BatchAuthors
            .Concat(LibraryAuthors)
            .Where(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredProviderId))
        {
            var preferred = matches.FirstOrDefault(a =>
                string.Equals(a.ProviderId, preferredProviderId, StringComparison.OrdinalIgnoreCase));
            if (preferred is not null)
                return (preferred.Name, preferred.ProviderId);
        }

        if (matches.Count == 1)
            return (matches[0].Name, matches[0].ProviderId);

        return (matches.FirstOrDefault()?.Name ?? id, null);
    }

    private static string AuthorKey(string? providerId, string authorId)
        => $"{providerId ?? ""}\u001F{authorId}";

    public async Task<ReverseImageSearchResult?> SearchSauceNaoForFetchFailedGroupAsync(
        ImportArtworkGroup group,
        CancellationToken ct) =>
        await SearchSauceNaoForFilesAsync(group.Files, ct);

    public async Task<ReverseImageSearchResult?> SearchSauceNaoForUnknownGroupAsync(
        ImportUnknownGroup group,
        CancellationToken ct) =>
        await SearchSauceNaoForFilesAsync(group.Files, ct);

    public async Task<ReverseImageSearchResult?> SearchSauceNaoForFilesAsync(
        IReadOnlyList<ImportItem> files,
        CancellationToken ct)
    {
        if (files.Count == 0)
            return null;

        // Reverse image search uploads the file, so it must never run on a
        // locally collected card. Nothing in this workspace sets a local
        // provider id any more — local import lives on its own page, which
        // offers no reverse search at all — so this is defence in depth
        // against a future path that does.
        if (files.All(f => LocalSourceIdentity.IsLocal(f.AuthorProviderId)))
            return null;

        var apiKey = GetReverseImageSearchApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("SauceNao API key is not set.");

        var result = await _importService.SearchReverseImageAsync(
            files[0].SourceFilePath,
            apiKey,
            ct);

        return result is null
            ? null
            : await PreferCurrentProviderAuthorAsync(result, ct);
    }

    private async Task<ReverseImageSearchResult> PreferCurrentProviderAuthorAsync(
        ReverseImageSearchResult result,
        CancellationToken ct)
    {
        if (result.ArtworkId is null)
            return result;

        var authorInfo = await _importService.FetchAuthorInfoAsync(
            new AuthorKey(result.ArtworkId.ProviderId, result.AuthorId),
            forceRefresh: true,
            ct);

        return authorInfo is null
            ? result
            : result with
            {
                AuthorName = authorInfo.Name,
                AuthorId = authorInfo.Key.Id,
            };
    }

    private string? GetReverseImageSearchApiKey()
    {
        if (_pluginService.ReverseImageSearchProvider is not IPluginSettingsProvider settingsProvider)
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
            ImportSearchResultAssignment.Apply(item, result, rating, ImportSearchResultAssignmentMode.FetchFailed);

        FetchFailedGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        AddBatchAuthorIfLoaded(result.AuthorName, result.AuthorId, result.ArtworkId?.ProviderId);

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    public async Task ApplySauceNaoResultToUnknownGroupAsync(
        ImportUnknownGroup group,
        ReverseImageSearchResult result,
        ContentRating rating)
    {
        var files = group.Files.ToList();
        if (files.Count == 0)
            return;

        CaptureUndo(ManualAssignmentSource.Unknown, files);

        foreach (var item in files)
            ImportSearchResultAssignment.Apply(item, result, rating, ImportSearchResultAssignmentMode.Unknown);

        UnknownGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        AddBatchAuthorIfLoaded(result.AuthorName, result.AuthorId, result.ArtworkId?.ProviderId);

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    private void AddBatchAuthorIfLoaded(string authorName, string authorId, string? providerId)
    {
        if (!_authorsLoaded) return;
        var key = AuthorKey(providerId, authorId);
        if (BatchAuthors.Any(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) return;
        var libraryMatch = LibraryAuthors.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (libraryMatch is not null)
            LibraryAuthors.Remove(libraryMatch);
        BatchAuthors.Add(new SelectableAuthor(authorName, authorId, providerId));
    }

    private void MoveToUnknown(ImportItem item)
    {
        _manualHistory.RememberBaseline(item);
        var groupId = $"Group {++_unknownGroupCounter}";
        var group = new ImportUnknownGroup(groupId, [item]);
        UnknownGroups.Add(group);

        if (!_authorsLoaded)
        {
            _authorsLoaded = true;
            LoadAvailableAuthorsAsync().Observe(_logger, "Import.ReloadAvailableAuthors");
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

        AddUnknownGroups(GroupUnknownItems(allUnknownItems));

        if (!_authorsLoaded && UnknownGroups.Count > 0)
        {
            _authorsLoaded = true;
            LoadAvailableAuthorsAsync().Observe(_logger, "Import.ReloadAvailableAuthors");
        }
    }

    [RelayCommand]
    private async Task AssignAuthorToUnknownGroupAsync(ImportUnknownGroup group)
    {
        if (!group.CanAssignAuthor) return;

        var files = group.Files.ToList();
        CaptureUndo(ManualAssignmentSource.Unknown, files);

        var id = group.ManualAuthorId.Trim();
        var author = ResolveAuthor(id, group.ManualAuthorProviderId);

        ImportAuthorAssignment.Apply(files, id, author.Name, author.ProviderId, ImportAuthorAssignmentMode.Unknown);

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

        await _artworkAssignment.ApplyAsync(files, artworkId, AnalyzingItems.Add, CancellationToken.None);

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
        var author = ResolveAuthor(id, BatchManualAuthorProviderId);

        ImportAuthorAssignment.Apply(files, id, author.Name, author.ProviderId, ImportAuthorAssignmentMode.Unknown);

        foreach (var group in groups)
            UnknownGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        BatchManualAuthorId = null;
        BatchManualAuthorProviderId = null;

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

        await _artworkAssignment.ApplyAsync(files, artworkId, AnalyzingItems.Add, CancellationToken.None);

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

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AddFilesAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0) return;

        var newPaths = filePaths
            .Where(p => Path.GetExtension(p).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Where(p => Items.All(existing => !string.Equals(existing.SourceFilePath, p, StringComparison.OrdinalIgnoreCase)))
            .Where(p => !_analysisTracker.ContainsPath(p))
            .ToList();

        if (newPaths.Count == 0) return;

        var batch = _analysisTracker.Begin(newPaths);
        UpdateAnalysisProgress();
        var rejected = 0;

        try
        {
            await _settingsLoaded;

            rejected = await _importService.AnalyzeAsync(newPaths, Items, _dispatcher, batch.Token);
            batch.AddRejectedCount(rejected);
            UpdateAnalysisProgress();

            // Compute fingerprints for subfolder decisions and optional unknown visual grouping.
            var newPathSet = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
            var newItems = Items.Where(i => newPathSet.Contains(i.SourceFilePath)).ToList();
            await _importService.ComputeFingerprintsAsync(newItems, batch.Token);

            // Flush buffered unknowns into groups (fingerprints already computed above)
            FlushPendingUnknowns();

            // Re-resolve destinations after publishing unknown groups. Destination
            // resolution can involve library I/O; it must not delay or suppress the
            // cards that already need manual identification.
            await ReResolveDestinationsAsync(batch.Token);

            if (rejected > 0)
                ShowRejectedFiles(rejected);
        }
        catch (OperationCanceledException ex) { _logger.LogError("Import.AnalysisCanceled", ex); }
        finally
        {
            batch.Dispose();
            UpdateAnalysisProgress();
            UpdateCounts();
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmImport))]
    private async Task ConfirmImportAsync()
    {
        if (!CanConfirmImport)
            return;

        var plans = BuildExecutionPlansFromWorkspace();
        if (plans.Count == 0)
        {
            TransactionStatusText = GetLocalizedString("Import_Summary_NoEligible");
            return;
        }

        ExecutionProgress = new ImportExecutionProgressSnapshot(
            ImportExecutionPhase.Executing,
            CompletedCount: 0,
            TotalCount: plans.Count,
            SuccessCount: 0,
            FailedCount: 0,
            ManualRecoveryRequiredCount: 0,
            WarningCount: 0,
            LastItemState: TransactionItemState.Prepared,
            LastFailureType: TransactionFailureType.None,
            LastWarningType: ImportExecutionWarningType.None);
        TransactionStatusText = GetLocalizedString("Import_Phase_Preparing");
        IsImporting = true;

        try
        {
            var result = await _executionCoordinator.ExecuteAsync(plans, snapshot =>
            {
                ExecutionProgress = snapshot;
                TransactionStatusText = snapshot.Phase switch
                {
                    ImportExecutionPhase.Executing => string.Format(
                        GetLocalizedString("Import_Phase_Executing"),
                        snapshot.CompletedCount,
                        snapshot.TotalCount),
                    ImportExecutionPhase.RollingBack => GetLocalizedString("Import_Phase_RollingBack"),
                    ImportExecutionPhase.Completed => GetLocalizedString("Import_Phase_Completed"),
                    _ => TransactionStatusText,
                };
            });

            if (result.Receipt.IsFullySuccessful)
            {
                _importService.RegisterCommittedLibraryFiles(result.CommittedPaths);
                ResetWorkspaceAfterCommittedTransaction();
                TransactionStatusText = string.Format(
                    GetLocalizedString("Import_Summary_Success"),
                    plans.Count);
            }
            else
            {
                TransactionStatusText = string.Format(
                    GetLocalizedString("Import_Summary_Failed"),
                    result.SafelyRolledBackCount,
                    result.FailedCount,
                    result.WarningCount,
                    result.ManualRecoveryCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Import.Transaction", ex);
            TransactionStatusText = GetLocalizedString("Import_Summary_UnexpectedError");
        }
        finally
        {
            IsImporting = false;
        }
    }

    [RelayCommand]
    private void CancelImport() => _executionCoordinator.Cancel();

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RefreshLibraryIndexAsync()
    {
        _importService.InvalidateLibraryFileCache();
        try
        {
            await ReResolveDestinationsAsync();
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogError("Import.RefreshLibraryIndexCanceled", ex);
        }
    }

    private IReadOnlyList<ImportItemPlan> BuildExecutionPlansFromWorkspace()
    {
        var plans = new List<ImportItemPlan>();
        foreach (var item in Items.Where(item => ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath)))
        {
            var document = item.FetchedArtworkInfo is null
                ? null
                : PostMetadataMapper.ToDocument(item.FetchedArtworkInfo, []);
            plans.Add(new ImportItemPlan(
                item.SourceFilePath,
                item.DestinationPath!,
                item.AuthorDirectoryPath,
                document));
        }

        return plans;
    }

    private void ResetWorkspaceAfterCommittedTransaction()
    {
        Items.Clear();
        UpdateCounts();
    }

    private static string GetLocalizedString(string key)
    {
        try
        {
            var value = ResLoader.GetString(key.Replace('.', '/'));
            return string.IsNullOrEmpty(value) ? key : value;
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80073B17))
        {
            // A missing UI string must never abort an import collection update.
            return key;
        }
    }

    [RelayCommand]
    private async Task AssignAuthorAsync(ImportArtworkGroup group)
    {
        if (!group.CanAssignAuthor) return;

        var files = group.Files.ToList();
        CaptureUndo(ManualAssignmentSource.FetchFailed, files);

        var authorId = group.ManualAuthorId.Trim();
        var author = ResolveAuthor(authorId, group.ManualAuthorProviderId);

        ImportAuthorAssignment.Apply(files, authorId, author.Name, author.ProviderId, ImportAuthorAssignmentMode.FetchFailedSingle);

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
        var author = ResolveAuthor(authorId, BatchFetchFailedAuthorProviderId);

        ImportAuthorAssignment.Apply(files, authorId, author.Name, author.ProviderId, ImportAuthorAssignmentMode.FetchFailedBatch);

        foreach (var group in groups)
            FetchFailedGroups.Remove(group);

        foreach (var item in files)
            PlaceItemInTree(item);

        BatchFetchFailedAuthorId = null;
        BatchFetchFailedAuthorProviderId = null;
        UpdateSelectedFetchFailedCount();

        await ReResolveDestinationsAsync();
        UpdateCounts();
    }

    [RelayCommand(CanExecute = nameof(CanUndoManualAssignment))]
    private async Task UndoManualAssignmentAsync()
    {
        var undo = _manualHistory.TakeUndo();
        if (undo is null) return;
        CanUndoManualAssignment = false;

        var unknownItems = new List<ImportItem>();
        foreach (var state in undo.Items)
        {
            RemoveItemFromManualContainers(state.Item);
            ImportManualAssignmentHistory.Restore(state);

            if (undo.Source == ManualAssignmentSource.FlatReview)
            {
                RemoveItemFromManualContainers(state.Item);
                if (!string.IsNullOrWhiteSpace(state.Item.AuthorId)) PlaceItemInTree(state.Item);
                else if (state.Item.ArtworkId is not null) MoveFetchFailed(state.Item);
                else unknownItems.Add(state.Item);
            }
            else if (undo.Source == ManualAssignmentSource.Unknown)
                unknownItems.Add(state.Item);
            else if (undo.Source == ManualAssignmentSource.FetchFailed)
                MoveFetchFailed(state.Item);
        }

        if (unknownItems.Count > 0)
        {
            AddUnknownGroups(GroupUnknownItems(unknownItems));

            if (!_authorsLoaded && UnknownGroups.Count > 0)
            {
                _authorsLoaded = true;
                LoadAvailableAuthorsAsync().Observe(_logger, "Import.RefreshAvailableAuthors");
            }
        }

        await ReResolveDestinationsAsync();
        UpdateCounts();
        ReviewBatchStatusText = GetLocalizedString("Import_Review_Undone");
    }

    [RelayCommand]
    private void Clear()
    {
        _analysisTracker.CancelAll();
        _importService.CancelPendingResolution();
        _executionCoordinator.Cancel();
        _manualHistory.Clear();
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
            DebouncedReResolveAsync().Observe(_logger, "Import.DebouncedResolve");
    }

    partial void OnUseVisualSimilarityChanged(bool value)
    {
        RegroupUnknowns();

        if (HasItems)
            DebouncedReResolveAsync().Observe(_logger, "Import.DebouncedResolve");
    }

    private List<List<ImportItem>> GroupUnknownItems(IReadOnlyList<ImportItem> items) =>
        UseVisualSimilarity
            ? CardGroupingService.GroupByVisualSimilarity(items)
            : items.Select(item => new List<ImportItem> { item }).ToList();

    private void AddUnknownGroups(IEnumerable<List<ImportItem>> groups)
    {
        foreach (var groupItems in groups)
        {
            var groupId = $"Group {++_unknownGroupCounter}";
            var group = new ImportUnknownGroup(groupId, groupItems);
            UnknownGroups.Add(group);
        }
    }

    private void RegroupUnknowns()
    {
        if (UnknownGroups.Count == 0) return;

        var allUnknownItems = UnknownGroups.SelectMany(group => group.Files).ToList();
        UnknownGroups.Clear();
        AddUnknownGroups(GroupUnknownItems(allUnknownItems));
    }

    private async Task DebouncedReResolveAsync()
    {
        try
        {
            await ReResolveDestinationsAsync(debounce: true);
        }
        catch (OperationCanceledException ex) { _logger.LogError("Import.DebouncedResolveCanceled", ex); }
    }

    private async Task ReResolveDestinationsAsync(CancellationToken ct = default, bool debounce = false)
    {
        var diagnostics = await _importService.ReResolveWithDetailedDiagnosticsAsync(
            Items,
            _dispatcher,
            ct,
            (int)ArtworkSubfolderThreshold,
            UseVisualSimilarity,
            debounce);
        Debug.WriteLine(
            $"Import.ResolveDiagnostics: bg={diagnostics.BackgroundIndexElapsedMs}ms, "
            + $"library-scan-index={diagnostics.BgLibraryScanAndCandidateIndexElapsedMs}ms, "
            + $"byte-compare={diagnostics.BgByteComparisonElapsedMs}ms, "
            + $"folder-index={diagnostics.BgFolderIndexElapsedMs}ms, "
            + $"candidates={diagnostics.TotalCandidatesCompared}, "
            + $"identical={diagnostics.ActualIdenticalDuplicates}, "
            + $"mismatches={diagnostics.ActualContentMismatches}, "
            + $"queue={diagnostics.UiQueueDelayMs}ms, "
            + $"artwork-io={diagnostics.UiArtworkDirectoryLookupElapsedMs}ms, "
            + $"property={diagnostics.UiPropertyAssignmentElapsedMs}ms, "
            + $"destination-updates={diagnostics.DestinationPathAssignments}, "
            + $"already-in-library={diagnostics.StatusTransitionsToAlreadyInLibrary}");
        ReconcileReviewStatesIfVisible();
    }

    private void UpdateAnalysisProgress()
    {
        var progress = _analysisTracker.GetProgress(Items
            .Where(item => item.Status != ImportItemStatus.Analyzing)
            .Select(item => item.SourceFilePath));
        AnalysisTotalCount = progress.TotalCount;
        AnalysisCompletedCount = progress.CompletedCount;
        IsAnalyzing = progress.IsAnalyzing;
    }

    partial void OnIsAnalyzingChanged(bool value)
    {
        OnPropertyChanged(nameof(AnalysisLoadingVisibility));
        OnPropertyChanged(nameof(UnknownEmptyVisibility));
        OnPropertyChanged(nameof(UnknownLoadingVisibility));
        if (!value)
            ReconcileReviewStates();
        else
            NotifyReviewWorkspaceState();
    }

    [RelayCommand]
    private void SetReviewFilter(string filter) => _reviewWorkspace.SetFilter(filter);

    [RelayCommand]
    private void RemoveReviewSelection()
    {
        if (!IsIdle) return;
        foreach (var state in ReviewedItemsView.OfType<ImportItemReviewState>().Where(s => s.IsSelected && s.CanSelect).ToArray())
        {
            RemoveItemFromManualContainers(state.Item);
            Items.Remove(state.Item);
        }
        ReconcileReviewStates(); UpdateCounts();
    }

    public async Task ApplySearchResultToSelectionAsync(IReadOnlyList<ImportItem> selected, ReverseImageSearchResult result, ContentRating rating)
    {
        if (!IsIdle) return;
        var current = selected.Where(Items.Contains).ToList();
        CaptureUndo(ManualAssignmentSource.FlatReview, current);
        foreach (var item in current)
        {
            RemoveItemFromManualContainers(item);
            ImportSearchResultAssignment.Apply(item, result, rating, ImportSearchResultAssignmentMode.FlatReview);
            PlaceItemInTree(item);
        }
        await ReResolveDestinationsAsync(); UpdateCounts();
    }

    private void ReconcileReviewStatesIfVisible()
    {
        ReconcileReviewStates();
    }

    private void ReconcileReviewStates() => _reviewWorkspace.Reconcile(Items);

    private void ClearReviewStates()
    {
        _reviewWorkspace.Clear();
        ReviewBatchStatusText = string.Empty;
        NotifyReviewWorkspaceState();
    }

    private void OnReviewWorkspaceChanged()
    {
        OnPropertyChanged(nameof(SelectedReviewFilter));
        OnPropertyChanged(nameof(ReviewFilterIndex));
        OnPropertyChanged(nameof(SelectedReviewCount));
        OnPropertyChanged(nameof(IsAllReviewSelected));
        OnPropertyChanged(nameof(HasSelectedReviewItems));
        OnPropertyChanged(nameof(SelectedVisibleReviewItems));
        OnPropertyChanged(nameof(ReviewSelectionEditorVisibility));
        OnPropertyChanged(nameof(ReviewSelectionPromptVisibility));
        NotifyReviewWorkspaceState();
    }

    private void OnReviewRowsChanged()
    {
        OnPropertyChanged(nameof(UnidentifiedNavigationText));
        OnPropertyChanged(nameof(UnavailableNavigationText));
        OnPropertyChanged(nameof(IdentifiedNavigationText));
    }

    private void NotifyReviewWorkspaceState()
    {
        OnPropertyChanged(nameof(ReviewFilterEmptyText));
        OnPropertyChanged(nameof(ClearReviewRatingFilterVisibility));
        OnPropertyChanged(nameof(FlatReviewWorkspaceVisibility));
        OnPropertyChanged(nameof(EmptyReviewWorkspaceVisibility));
        OnPropertyChanged(nameof(FilteredReviewEmptyVisibility));
        OnPropertyChanged(nameof(CanConfirmImport));
        OnPropertyChanged(nameof(IsReviewWorkspaceEnabled));
        OnPropertyChanged(nameof(ImportableCount));
        ConfirmImportCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ReviewSummaryText));
        OnPropertyChanged(nameof(ImportActionText));
        OnPropertyChanged(nameof(ReviewSelectionText));
        OnPropertyChanged(nameof(ReviewEmptyTitle));
        OnPropertyChanged(nameof(ReviewEmptyDescription));
    }

    public void SetReviewViewportWidth(double width) => _reviewWorkspace.SetReviewViewportWidth(width);

    public void ToggleReviewRow(ImportReviewRow row)
    {
        if (IsReviewWorkspaceEnabled) _reviewWorkspace.Toggle(row.Items);
    }

    public void ToggleReviewGroup(ImportReviewGroup group)
    {
        if (IsReviewWorkspaceEnabled) _reviewWorkspace.Toggle(group);
    }

    [RelayCommand] private Task FetchReviewArtworkAsync() => ApplyReviewChangesAsync(true, false, false);
    [RelayCommand] private Task ApplyReviewAuthorAsync() => ApplyReviewChangesAsync(false, true, false);
    [RelayCommand] private Task ApplyReviewRatingAsync() => ApplyReviewChangesAsync(false, false, true);

    [RelayCommand]
    private Task ApplyReviewBatchOverrideAsync() => ApplyReviewChangesAsync(true, true, true);

    private async Task ApplyReviewChangesAsync(bool applyArtwork, bool applyAuthor, bool applyRating)
    {
        if (!IsReviewWorkspaceEnabled) return;
        var selected = SelectedVisibleReviewItems;
        if (selected.Count == 0) return;

        var request = new ImportReviewEditRequest(applyArtwork, applyAuthor, applyRating,
            ReviewArtworkId, ReviewArtworkProviderId, ReviewAuthorId, ReviewAuthorProviderId,
            ReviewRatingOverrideIndex);
        var validation = ImportReviewEditService.Validate(request);
        if (validation != ImportReviewEditStatus.Ready)
        {
            ReviewBatchStatusText = ReviewEditStatusText(validation);
            return;
        }

        IsResolvingReview = true;
        ReviewBatchStatusText = GetLocalizedString("Import_Review_Working");
        try
        {
            var result = await _reviewEditService.PrepareAsync(request, CancellationToken.None);
            if (result.Status != ImportReviewEditStatus.Ready)
            {
                ReviewBatchStatusText = ReviewEditStatusText(result.Status);
                return;
            }

            CaptureUndo(ManualAssignmentSource.FlatReview, selected);
            foreach (var item in selected)
            {
                if (result.Edit!.Apply(item))
                {
                    RemoveItemFromManualContainers(item);
                    item.ErrorMessage = null;
                    item.Status = ImportItemStatus.ReadyToImport;
                    PlaceItemInTree(item);
                }
            }

            await ReResolveDestinationsAsync();
            UpdateCounts();
            foreach (var state in ReviewStates.Where(state => state.IsSelected))
                state.IsSelected = false;
            if (applyAuthor) { ReviewAuthorProviderId = null; ReviewAuthorId = null; }
            if (applyArtwork) { ReviewArtworkProviderId = null; ReviewArtworkId = null; }
            if (applyRating) ReviewRatingOverrideIndex = 0;
            ReviewBatchStatusText = GetLocalizedString("Import_Review_BatchApplied");
            ReconcileReviewStatesIfVisible();
        }
        catch (Exception ex)
        {
            _logger.LogError("Import.ReviewEdit", ex);
            ReviewBatchStatusText = GetLocalizedString("Import_Review_ActionFailed");
        }
        finally { IsResolvingReview = false; }
    }

    private static string ReviewEditStatusText(ImportReviewEditStatus status) => GetLocalizedString(status switch
    {
        ImportReviewEditStatus.IdentityPairRequired => "Import_Review_IdentityPairRequired",
        ImportReviewEditStatus.NoChanges => "Import_Review_NoChanges",
        ImportReviewEditStatus.InputUnresolved => "Import_Review_InputUnresolved",
        ImportReviewEditStatus.FetchUnchanged => "Import_Review_FetchUnchanged",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    });

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
        ReadyWithoutDestinationCount = Items.Count(i => i.Status == ImportItemStatus.ReadyToImport
                                                        && string.IsNullOrWhiteSpace(i.DestinationPath));
    }

    private void CaptureUndo(ManualAssignmentSource source, IReadOnlyList<ImportItem> items)
    {
        _manualHistory.Capture(source, items);
        CanUndoManualAssignment = true;
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

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
    private bool _suspendItemCollectionRefresh;

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

    // Why the last import was rolled back. The progress overlay that shows the phase text goes
    // away when the rollback ends, so without this the user is left with no explanation.
    [ObservableProperty]
    public partial bool ShowImportFailure { get; set; }

    [ObservableProperty]
    public partial string ImportFailureTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ImportFailureMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool HasItems { get; set; }

    public bool IsEmpty => !HasItems;
    public bool IsIdle => !IsAnalyzing && !IsImporting;
    public bool IsReviewWorkspaceEnabled => IsIdle && !IsResolvingReview;
    [ObservableProperty]
    public partial bool IsResolvingReview { get; set; }
    partial void OnIsResolvingReviewChanged(bool value)
    {
        NotifyReviewWorkspaceState();
        // A batch that finished during a review edit left its candidates waiting.
        if (!value)
            AutoImportIdentifiedAsync().Observe(_logger, "Import.AutoImport");
    }
    public int ImportableCount => Items.Count(item => ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath));
    public bool CanConfirmImport => IsReviewWorkspaceEnabled && ImportableCount > 0 && !IsArtworkLookupPending;

    /// <summary>
    /// A post URL typed but not yet applied. Importing then would file the
    /// cards it was meant for without that post, so the import waits until a
    /// fetch succeeds (which empties the box) or the user clears it. A failed
    /// fetch leaves the text, and so the cards still unidentified, in place.
    /// </summary>
    [ObservableProperty]
    public partial bool IsArtworkLookupPending { get; set; }
    partial void OnIsArtworkLookupPendingChanged(bool value) => NotifyReviewWorkspaceState();

    /// <summary>
    /// Importable cards with no identified author. They go to the unknown
    /// folder, which the user should agree to rather than discover later.
    /// </summary>
    public int UnidentifiedImportableCount => Items.Count(item =>
        ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath)
        && !ImportReviewPolicy.IsIdentified(item.Status, !string.IsNullOrWhiteSpace(item.AuthorId)));
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
    public string ImportActionText => IsArtworkLookupPending
        ? GetLocalizedString("Import_Action_LookupPending")
        : string.Format(GetLocalizedString("Import_ActionCount"), ImportableCount);
    public string ImportCompactActionText => IsArtworkLookupPending
        ? GetLocalizedString("Import_Action_LookupPendingCompact")
        : string.Format(GetLocalizedString("Import_ActionCountCompact"), ImportableCount);
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
    // Only an empty box clears it: the user emptied it, or a fetch succeeded.
    partial void OnReviewArtworkIdChanged(string? value)
    {
        IsArtworkLookupPending = !string.IsNullOrWhiteSpace(value);
        if (_autoFilledValue is not null && value?.Trim() != _autoFilledValue) _autoFilledValue = null;
        ScheduleLinkMatch(value);
    }

    // What the post's download links list, compared with the cards in the list.
    [ObservableProperty]
    public partial string LinkMatchText { get; set; } = string.Empty;
    public Visibility LinkMatchVisibility => string.IsNullOrWhiteSpace(LinkMatchText) ? Visibility.Collapsed : Visibility.Visible;
    partial void OnLinkMatchTextChanged(string value) => OnPropertyChanged(nameof(LinkMatchVisibility));

    [ObservableProperty]
    public partial string LinkMissingText { get; set; } = string.Empty;
    public Visibility LinkMissingVisibility => string.IsNullOrWhiteSpace(LinkMissingText) ? Visibility.Collapsed : Visibility.Visible;
    partial void OnLinkMissingTextChanged(string value) => OnPropertyChanged(nameof(LinkMissingVisibility));

    // The files behind the two texts below, with their addresses, so the page can make each name a link.
    [ObservableProperty]
    public partial IReadOnlyList<ListedFile> LinkMissingFiles { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<ListedFile> LinkInLibraryFiles { get; set; } = [];

    // Folders of the share that nothing has been taken from: episodes not being worked on.
    [ObservableProperty]
    public partial IReadOnlyList<UnstartedFolder> LinkUnstartedFolders { get; set; } = [];

    [ObservableProperty]
    public partial string LinkUnstartedText { get; set; } = string.Empty;
    public Visibility LinkUnstartedVisibility => string.IsNullOrWhiteSpace(LinkUnstartedText) ? Visibility.Collapsed : Visibility.Visible;
    partial void OnLinkUnstartedTextChanged(string value) => OnPropertyChanged(nameof(LinkUnstartedVisibility));

    // Listed files that no dropped card covers but the library already holds identically.
    [ObservableProperty]
    public partial string LinkInLibraryText { get; set; } = string.Empty;
    public Visibility LinkInLibraryVisibility => string.IsNullOrWhiteSpace(LinkInLibraryText) ? Visibility.Collapsed : Visibility.Visible;
    partial void OnLinkInLibraryTextChanged(string value) => OnPropertyChanged(nameof(LinkInLibraryVisibility));

    internal const int MissingNamesShown = 8;
    private static readonly TimeSpan LinkMatchDebounce = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan LinkMatchFetchWait = TimeSpan.FromSeconds(3);
    private readonly PostLinkLookup _postLinkLookup;
    private CancellationTokenSource? _linkMatchCts;
    private Task _linkMatchTask = Task.CompletedTask;

    // The links of the post last looked up, so cards dropped in afterwards are matched at once.
    private (string Input, string PostId, IReadOnlyList<PostLinkInfo> Links)? _linkCache;

    // A fetch that succeeds empties the box, but the result of the match is still
    // wanted afterwards, so only the user emptying the box clears it.
    private bool _keepLinkMatchOnClear;

    private void ScheduleLinkMatch(string? input)
    {
        _linkMatchCts?.Cancel();
        _linkMatchCts?.Dispose();
        _linkMatchCts = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            if (!_keepLinkMatchOnClear) ClearLinkMatch();
            return;
        }
        if (!_postLinkLookup.HasInspectors
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return;

        var cts = _linkMatchCts = new CancellationTokenSource();
        _linkMatchTask = RunLinkMatchAsync(input.Trim(), cts.Token);
    }

    // The lookup found nothing to apply (no readable links, or no dropped card among the files).
    // An address the clipboard put in the box would then sit there, hidden while no card is
    // selected, with the import button waiting on it, so take it back and keep the answer.
    private void TakeBackAutoFilledAddress(string input)
    {
        if (_autoFilledValue is null || _autoFilledValue != input || ReviewArtworkId?.Trim() != input) return;

        _keepLinkMatchOnClear = true;
        try { ReviewArtworkId = null; }
        finally { _keepLinkMatchOnClear = false; }
        _autoFilledValue = null;
        _lastOfferedClipboardText = null; // copying the same address again is a new request
    }

    private void ClearLinkMatch()
    {
        _linkCache = null;
        LinkMatchText = string.Empty;
        LinkMissingFiles = [];
        LinkInLibraryFiles = [];
        LinkUnstartedFolders = [];
        LinkUnstartedText = string.Empty;
        LinkMissingText = string.Empty;
        LinkInLibraryText = string.Empty;
    }

    // Runs in the background while the user looks at the post; nothing waits for it,
    // and a post with no readable links leaves the page exactly as it was.
    private async Task RunLinkMatchAsync(string input, CancellationToken ct)
    {
        try
        {
            string postId;
            IReadOnlyList<PostLinkInfo> links;
            if (_linkCache is { } cached && cached.Input == input)
                (postId, links) = (cached.PostId, cached.Links);
            else
            {
                await Task.Delay(LinkMatchDebounce, ct);
                var artworkId = _importService.ResolveReviewArtworkInput(input, ReviewArtworkProviderId);
                if (artworkId is null) return;

                var info = await _importService.FetchArtworkInfoAsync(artworkId, ct);
                if (info is null)
                {
                    ShowLinkStatus("Import_LinkMatch_NoPost", artworkId.Id);
                    TakeBackAutoFilledAddress(input);
                    return;
                }
                postId = info.ArtworkId.Id;
                links = await _postLinkLookup.InspectAsync(info.Description, ct);
                if (ct.IsCancellationRequested) return;
                if (links.Count == 0)
                {
                    ShowLinkStatus("Import_LinkMatch_NoLinks", postId);
                    TakeBackAutoFilledAddress(input);
                    return;
                }
                _linkCache = (input, postId, links);
            }

            // The list can hold thousands of cards: take the names here, on the UI thread,
            // and do the comparing, which runs a pattern per name, somewhere else.
            var cardNames = ReviewStates.Where(state => state.CanSelect).Select(state => state.Item.FileName).ToList();
        // Single-file links first: a big folder would otherwise fill the few names shown.
            var listed = links.OrderBy(link => link.IsFolder).SelectMany(link => link.Files).Select(file => new ListedFile(file.Name, file.Size, file.Url, file.Folder, file.FolderUrl)).ToList();
            var match = await Task.Run(() => PostLinkMatcher.MatchFiles(listed, cardNames, libraryHas: null), ct);
            if (ct.IsCancellationRequested) return;
            if (match.ListedCount == 0)
            {
                ShowLinkStatus("Import_LinkMatch_NoLinks", postId);
                TakeBackAutoFilledAddress(input);
                return;
            }

            if (match.Matched.Count > 0)
            {
                var matched = new HashSet<string>(match.Matched, StringComparer.OrdinalIgnoreCase);
                _reviewWorkspace.SetSelection(state => matched.Contains(state.Item.FileName));
            }

            ShowLinkMatch(postId, match);

            // The library scan can take a while the first time, so the selection and the
            // first answer above do not wait for it; the answer is refined once it is in.
            var inLibrary = await TryGetLibraryLookupAsync(ct);
            if (inLibrary is not null && !ct.IsCancellationRequested)
            {
                // Asks the disk about each candidate's size, so also off the UI thread.
                var refined = await Task.Run(() => PostLinkMatcher.MatchFiles(listed, cardNames, inLibrary), ct);
                if (ct.IsCancellationRequested) return;
                if (refined.InLibrary.Count > 0) ShowLinkMatch(postId, refined);
            }

            // Last, because emptying the box cancels this run. Nothing was selected, so
            // there is nothing for the user to apply.
            if (match.Matched.Count == 0) TakeBackAutoFilledAddress(input);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError("Import.LinkMatch", ex);
        }
    }

    private async Task<Func<string, long?, bool>?> TryGetLibraryLookupAsync(CancellationToken ct)
    {
        try { return await _importService.GetLibraryLookupAsync(ct); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError("Import.LinkMatch.Library", ex);
            return null;
        }
    }

    internal static string NamesWithMore(IReadOnlyList<string> names)
        => string.Join(", ", names.Take(MissingNamesShown))
           + (names.Count > MissingNamesShown
               ? string.Format(GetLocalizedString("Import_LinkMatch_MissingMore"), names.Count - MissingNamesShown)
               : string.Empty);

    // Says why nothing was matched: silence would look like the feature is broken.
    private void ShowLinkStatus(string resourceKey, string postId)
    {
        LinkMatchText = string.Format(GetLocalizedString(resourceKey), postId);
        LinkMissingFiles = [];
        LinkInLibraryFiles = [];
        LinkUnstartedFolders = [];
        LinkUnstartedText = string.Empty;
        LinkMissingText = string.Empty;
        LinkInLibraryText = string.Empty;
    }

    private void ShowLinkMatch(string artworkId, PostLinkMatch match)
    {
        var missing = match.Missing.Count;
        var inLibrary = match.InLibrary.Count;
        LinkMatchText = match.Matched.Count == 0
            ? string.Format(GetLocalizedString("Import_LinkMatch_NoMatch"), artworkId, match.ListedCount)
            : missing == 0 && inLibrary == 0 && match.Unlisted.Count == 0 && match.UnstartedFolders.Count == 0
                ? string.Format(GetLocalizedString("Import_LinkMatch_Complete"), artworkId, match.ListedCount, match.Matched.Count)
                : string.Format(GetLocalizedString("Import_LinkMatch_Summary"),
                    artworkId, match.ListedCount, match.Matched.Count, missing, match.Unlisted.Count);

        // The files first: the page rebuilds its links when the text changes and reads them then.
        LinkMissingFiles = match.MissingFiles;
        LinkInLibraryFiles = match.InLibraryFiles;
        LinkUnstartedFolders = match.UnstartedFolders;
        LinkUnstartedText = match.UnstartedFolders.Count == 0 ? string.Empty : GetLocalizedString("Import_LinkMatch_Unstarted");
        LinkMissingText = missing == 0 ? string.Empty : string.Format(
            GetLocalizedString("Import_LinkMatch_Missing"), NamesWithMore(match.Missing));
        LinkInLibraryText = inLibrary == 0 ? string.Empty : string.Format(
            GetLocalizedString("Import_LinkMatch_InLibrary"), NamesWithMore(match.InLibrary));
    }

    // ── Checking an author's posts against the cards in the list ───────────────

    /// <summary>Whether an author check can work: something lists an author's posts and something reads their links.</summary>
    public bool CanCheckAuthors => _pluginService.AuthorPostListers.Count > 0 && _postLinkLookup.HasInspectors;
    public Visibility AuthorCheckVisibility => CanCheckAuthors ? Visibility.Visible : Visibility.Collapsed;

    public bool HasReviewCards => Items.Count > 0;

    /// <summary>File names of every card in the list, for the author check.</summary>
    public IReadOnlyList<string> ReviewCardNames => Items.Select(item => item.FileName).ToList();

    /// <summary>The author an address points at, or null when it is not an author page of any installed platform.</summary>
    public AuthorKey? TryParseAuthorUrl(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return null;
        foreach (var lister in _pluginService.AuthorPostListers)
        {
            try { if (lister.TryParseAuthorUrl(value) is { } key) return key; }
            catch (Exception ex) { _logger.LogError("Import.AuthorUrl", ex); }
        }
        return null;
    }

    internal async Task<AuthorCheckResult> CheckAuthorPostsAsync(
        AuthorKey author, IProgress<AuthorCheckProgress> progress, CancellationToken ct)
    {
        var cards = ReviewCardNames; // taken before the first await, while still on the UI thread
        var lister = _pluginService.AuthorPostListers.FirstOrDefault(candidate =>
            candidate.ProviderId.Equals(author.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (lister is null)
            return new AuthorCheckResult(AuthorCheckStatus.ListFailed, 0, 0, 0, 0, [], cards);

        var libraryHas = await TryGetLibraryLookupAsync(ct);
        var checker = new AuthorPostChecker(
            lister.ListPostsAsync,
            (id, token) => _importService.FetchArtworkInfoAsync(id, token),
            (description, token) => _postLinkLookup.InspectAsync(description, token));
        return await checker.CheckAsync(author, cards, libraryHas, progress, ct);
    }

    /// <summary>
    /// Fetches each post an author check found and applies it to the dropped cards that post
    /// lists: the same thing the post-address box does for the selected cards, for every post
    /// of the check at once. Returns how many cards and how many posts were applied.
    /// </summary>
    internal async Task<(int Cards, int Posts)> ApplyAuthorCheckAsync(IReadOnlyList<AuthorPostCheckEntry> entries)
    {
        if (!IsReviewWorkspaceEnabled || entries.Count == 0) return (0, 0);

        IsResolvingReview = true;
        ReviewBatchStatusText = GetLocalizedString("Import_Review_Working");
        int cards = 0, posts = 0;
        try
        {
            foreach (var entry in entries)
            {
                // Only the cards this post owns: one that a more specific post also lists goes there,
                // not to the post that links the whole archive.
                var names = new HashSet<string>(entry.ClaimedCards ?? [], StringComparer.OrdinalIgnoreCase);
                if (names.Count == 0) continue;
                var items = Items
                    .Where(item => !item.IsAlreadyInLibrary && names.Contains(item.FileName))
                    .ToList();
                if (items.Count == 0) continue;

                var request = new ImportReviewEditRequest(
                    true, false, false, entry.Post.Id, entry.Post.ProviderId, null, null, 0);
                if (ImportReviewEditService.Validate(request) != ImportReviewEditStatus.Ready) continue;

                var prepared = await _reviewEditService.PrepareAsync(request, CancellationToken.None);
                if (prepared.Status != ImportReviewEditStatus.Ready) continue;

                CaptureUndo(ManualAssignmentSource.FlatReview, items);
                var applied = 0;
                foreach (var item in items)
                {
                    if (!prepared.Edit!.Apply(item)) continue;
                    RemoveItemFromManualContainers(item);
                    item.ErrorMessage = null;
                    item.Status = ImportItemStatus.ReadyToImport;
                    PlaceItemInTree(item);
                    applied++;
                }
                if (applied > 0) { cards += applied; posts++; }
            }

            await ReResolveDestinationsAsync();
            UpdateCounts();
            _reviewWorkspace.SetSelection(_ => false);
            ReviewBatchStatusText = GetLocalizedString("Import_Review_BatchApplied");
            ReconcileReviewStatesIfVisible();
        }
        catch (Exception ex)
        {
            _logger.LogError("Import.AuthorCheckApply", ex);
            ReviewBatchStatusText = GetLocalizedString("Import_Review_ActionFailed");
            throw;
        }
        finally { IsResolvingReview = false; }
        return (cards, posts);
    }

    // Enter right after a paste: let the match that is already running finish, so the
    // fetch applies to the cards it selected. Bounded, because the fetch must not stall.
    private async Task WaitForLinkMatchAsync()
    {
        if (_linkMatchTask.IsCompleted) return;
        try { await _linkMatchTask.WaitAsync(LinkMatchFetchWait); }
        catch (TimeoutException) { }
    }

    // Remembered so a box the user has emptied is not refilled by the same clipboard text.
    private string? _lastOfferedClipboardText;

    // What the clipboard put in the box and nobody has touched since. Such text is ours to
    // replace or take back; text the user typed or pasted is theirs and is left alone.
    private string? _autoFilledValue;

    // Fills the post-URL box from clipboard text. Fetching and applying stay with the user.
    // Returns true when the box took the text.
    public bool TryFillArtworkFromClipboard(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value) || value == _lastOfferedClipboardText) return false;
        // A box holding an address the clipboard filled earlier does not block a newer one: the
        // editor row that holds the box is only shown while cards are selected, so a stale
        // address could otherwise sit there unseen and stop every later copy.
        var boxIsOurs = _autoFilledValue is not null && ReviewArtworkId?.Trim() == _autoFilledValue;
        if (!IsReviewWorkspaceEnabled || ReviewStates.Count == 0
            || (!string.IsNullOrWhiteSpace(ReviewArtworkId) && !boxIsOurs)) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        if (_importService.ResolveReviewArtworkInput(value, null) is null) return false;

        _lastOfferedClipboardText = value;
        ReviewArtworkId = value;
        _autoFilledValue = value; // after the assignment: changing the box resets it
        return true;
    }

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

        _postLinkLookup = new PostLinkLookup(pluginService.PostLinkInspectors, logger);
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
        if (_suspendItemCollectionRefresh) return;
        // Adding files raises this once per file. The analysis state has to be right at once
        // (it decides whether the loading indicator shows); the counts and the review list are
        // refreshed once per dispatcher turn, not once per file.
        UpdateAnalysisProgress();
        QueueItemRefresh();
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

    // Every property an import step sets on a card lands here. A step touches hundreds of
    // cards and several properties on each, and each landing used to recount every card and
    // re-reconcile the whole review list, which is quadratic and froze the page. The counts
    // and the list are only read once the step is over, so one pass per dispatcher turn serves.
    private bool _itemRefreshQueued;

    private void QueueItemRefresh()
    {
        if (_itemRefreshQueued) return;
        _itemRefreshQueued = true;
        if (!_dispatcher.TryEnqueue(RunItemRefresh))
            RunItemRefresh();
    }

    private void RunItemRefresh()
    {
        _itemRefreshQueued = false;
        UpdateCounts();
        ReconcileReviewStatesIfVisible();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not ImportItem item) return;
        if (e.PropertyName == nameof(ImportItem.Rating))
        {
            QueueItemRefresh();
            return;
        }
        if (e.PropertyName != nameof(ImportItem.Status))
        {
            if (e.PropertyName is nameof(ImportItem.DestinationPath) or nameof(ImportItem.ArtworkId))
                QueueItemRefresh();
            return;
        }
        UpdateAnalysisProgress();
        if (item.Status != ImportItemStatus.ReadyToImport)
        {
            QueueItemRefresh();
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
        QueueItemRefresh();
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

            // Only an id read from the file name counts: the user has not
            // touched these cards yet, so nothing about them came from a guess.
            foreach (var item in newItems.Where(static item => item.ArtworkId is not null))
                _autoImportCandidates.Add(item);

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

        await AutoImportIdentifiedAsync();
    }

    // Cards from finished batches whose file name gave an artwork id. Checked
    // once nothing else runs, then cleared; whatever does not qualify stays
    // in the review workspace like any other card.
    private readonly HashSet<ImportItem> _autoImportCandidates = [];

    /// <summary>
    /// Imports the candidates whose post was found, without waiting for the
    /// user to confirm them.
    /// </summary>
    /// <remarks>
    /// Waits for every batch to finish, because another batch's destination
    /// resolution would otherwise run over cards being moved. The last batch
    /// to finish, or the import that was running, picks up the rest.
    /// </remarks>
    private async Task AutoImportIdentifiedAsync()
    {
        if (_autoImportCandidates.Count == 0 || !IsReviewWorkspaceEnabled)
            return;

        var config = await _settingsService.LoadConfigAsync();
        if (!config.AutoImportIdentifiedPosts)
        {
            _autoImportCandidates.Clear();
            return;
        }
        if (!IsReviewWorkspaceEnabled)
            return;

        var eligible = _autoImportCandidates
            .Where(item => Items.Contains(item)
                && ImportReviewPolicy.CanAutoImport(
                    item.Status,
                    item.DestinationPath,
                    idFromFileName: item.ArtworkId is not null,
                    postFound: item.FetchedArtworkInfo is not null,
                    hasAuthor: !string.IsNullOrWhiteSpace(item.AuthorId)))
            .ToArray();
        _autoImportCandidates.Clear();
        if (eligible.Length == 0)
            return;

        await ExecuteImportAsync(eligible, "Import_Summary_AutoSuccess");
    }

    [RelayCommand(CanExecute = nameof(CanConfirmImport))]
    private Task ConfirmImportAsync() => ImportAsync(includeUnidentified: true);

    /// <summary>
    /// Imports every importable card, or only the identified ones; the rest
    /// stay in the review workspace.
    /// </summary>
    public async Task ImportAsync(bool includeUnidentified)
    {
        if (!CanConfirmImport)
            return;

        var importItems = Items
            .Where(item => ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath)
                && (includeUnidentified
                    || ImportReviewPolicy.IsIdentified(item.Status, !string.IsNullOrWhiteSpace(item.AuthorId))))
            .ToArray();
        await ExecuteImportAsync(importItems, "Import_Summary_Success");
    }

    // The rollback line used to say "conflict or cancelled" whatever had actually gone wrong.
    // The failure type is known by then, so say it.
    private static string RollingBackText(TransactionFailureType failure)
    {
        var text = GetLocalizedString("Import_Phase_RollingBack");
        return failure is TransactionFailureType.None or TransactionFailureType.Cancelled
            ? text
            : text + Environment.NewLine + string.Format(GetLocalizedString("Import_Failure_" + failure), string.Empty);
    }

    // Names the file that stopped the import and why, and keeps the whole list in the log.
    private void ReportImportFailure(ImportExecutionResult result)
    {
        var summary = ImportFailureSummary.From(result.Receipt.ItemReceipts);
        if (summary is null) return; // cancelled by the user: nothing to explain

        var first = summary.First;
        var lines = new List<string>
        {
            string.Format(GetLocalizedString("Import_Failure_" + first.FailureType), first.TargetFilePath),
            string.Format(GetLocalizedString("Import_Failure_Source"), first.SourceFilePath),
            string.Format(GetLocalizedString("Import_Failure_Target"), first.TargetFilePath),
            string.Format(GetLocalizedString("Import_Failure_Outcome"), result.SafelyRolledBackCount, result.ManualRecoveryCount),
        };
        if (summary.Total > 1)
            lines.Add(string.Format(GetLocalizedString("Import_Failure_More"), summary.Total - 1));
        lines.Add(GetLocalizedString("Import_Failure_Log"));

        ImportFailureTitle = GetLocalizedString("Import_Failure_Title");
        ImportFailureMessage = string.Join("\n", lines);
        ShowImportFailure = true;

        var everything = string.Join("; ", result.Receipt.ItemReceipts
            .Where(r => r.FailureType is not (TransactionFailureType.None or TransactionFailureType.Cancelled))
            .Select(r => $"{r.FailureType}: {r.SourceFilePath} -> {r.TargetFilePath}"));
        _ = Task.Run(() => _logger.LogError("Import.TransactionFailed", new InvalidOperationException(everything)));
    }

    private async Task ExecuteImportAsync(ImportItem[] importItems, string successResource)
    {
        var plans = await BuildExecutionPlansFromWorkspaceAsync(importItems);
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
        ShowImportFailure = false;
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
                    ImportExecutionPhase.RollingBack => RollingBackText(snapshot.LastFailureType),
                    ImportExecutionPhase.Completed => GetLocalizedString("Import_Phase_Completed"),
                    _ => TransactionStatusText,
                };
            });

            if (result.Receipt.IsFullySuccessful)
            {
                _importService.RegisterCommittedLibraryFiles(result.CommittedPaths);
                RemoveItemsFromWorkspace(importItems);
                TransactionStatusText = string.Format(
                    GetLocalizedString(successResource),
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
                ReportImportFailure(result);
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

        // Batches that finished while this ran left their candidates waiting.
        await AutoImportIdentifiedAsync();
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

    /// <summary>
    /// Turns the items into plans, giving a card a different name when another card
    /// in the batch already claimed its destination.
    /// </summary>
    /// <remarks>
    /// Two download folders often hold a card of the same name. Both resolve to one
    /// destination, and the executor stops at the second: the whole batch rolled
    /// back, and the user left to hunt the pair down by hand. So the clash is settled
    /// here instead. The same card — same card data, whatever its preview — keeps the
    /// shared destination, where the executor folds it as a duplicate; a different
    /// card gets a suffixed name, as a local import already does.
    ///
    /// Only clashes inside the batch are renamed. A library file of that name with a
    /// different card still stops the import, as before.
    ///
    /// Reads file contents for clashing names, so it runs off the UI thread.
    /// </remarks>
    private static Task<List<ImportItemPlan>> BuildExecutionPlansFromWorkspaceAsync(IReadOnlyList<ImportItem> importItems)
    {
        var snapshot = importItems
            .Select(item => (
                item.SourceFilePath,
                Destination: item.DestinationPath!,
                item.AuthorDirectoryPath,
                Document: PostMetadataMapper.ToImportDocument(item, [])))
            .ToList();

        return Task.Run(() =>
        {
            var claimed = new Dictionary<string, string>(PathComparison.Comparer);
            var plans = new List<ImportItemPlan>(snapshot.Count);
            foreach (var (source, destination, authorDirectory, document) in snapshot)
            {
                var resolved = destination;
                if (claimed.ContainsKey(destination))
                {
                    resolved = LocalDestinationNames.Resolve(
                        source, destination, claimed, File.Exists, IsSameCard);
                }
                else
                {
                    claimed[destination] = source;
                }

                plans.Add(new ImportItemPlan(source, resolved, authorDirectory, document)
                {
                    MatchCardData = true,
                });
            }

            return plans;
        });
    }

    private static bool IsSameCard(string left, string right)
    {
        try
        {
            return ImportDuplicateDetector.AreFilesIdentical(left, right)
                   || CardPayloadHash.AreSameCard(left, right);
        }
        catch (IOException)
        {
            return false;
        }
    }

    // One recount for the whole batch: removing cards one at a time recounted and re-reconciled
    // the list after each, which for a thousand cards is a thousand passes over a thousand cards.
    private void RemoveItemsFromWorkspace(IReadOnlyList<ImportItem> committedItems)
    {
        _suspendItemCollectionRefresh = true;
        try
        {
            foreach (var item in committedItems)
            {
                RemoveItemFromManualContainers(item);
                Items.Remove(item);
            }
        }
        finally
        {
            _suspendItemCollectionRefresh = false;
            CanUndoManualAssignment = _manualHistory.RetainOnly(Items);
            UpdateCounts();
            UpdateAnalysisProgress();
            ReconcileReviewStatesIfVisible();
        }
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
        if (value)
        {
            // New cards are coming: a result kept from the previous post no longer describes the list.
            if (string.IsNullOrWhiteSpace(ReviewArtworkId)) ClearLinkMatch();
        }

        OnPropertyChanged(nameof(AnalysisLoadingVisibility));
        OnPropertyChanged(nameof(UnknownEmptyVisibility));
        OnPropertyChanged(nameof(UnknownLoadingVisibility));
        if (!value)
        {
            ReconcileReviewStates();
            // After the reconcile: a match started earlier would see the list without the new cards.
            // A URL is waiting, so match the cards that just arrived.
            if (!string.IsNullOrWhiteSpace(ReviewArtworkId))
                ScheduleLinkMatch(ReviewArtworkId);
        }
        else
            NotifyReviewWorkspaceState();
    }

    [RelayCommand]
    private void SetReviewFilter(string filter) => _reviewWorkspace.SetFilter(filter);

    [RelayCommand]
    private void RemoveReviewSelection()
    {
        if (!IsIdle) return;
        var selected = ReviewedItemsView.OfType<ImportItemReviewState>()
            .Where(s => s.IsSelected && s.CanSelect).Select(s => s.Item).ToArray();
        RemoveItemsFromWorkspace(selected);
    }

    [RelayCommand]
    private void RemoveReviewItem(ImportItemReviewState? state)
    {
        if (!IsIdle || state is null || !Items.Contains(state.Item)) return;
        RemoveItemsFromWorkspace([state.Item]);
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
        ClearLinkMatch();
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
        OnPropertyChanged(nameof(ImportCompactActionText));
        OnPropertyChanged(nameof(ReviewSelectionText));
        OnPropertyChanged(nameof(ReviewEmptyTitle));
        OnPropertyChanged(nameof(ReviewEmptyDescription));
    }

    public void SetReviewViewportWidth(double width) => _reviewWorkspace.SetReviewViewportWidth(width);

    public void ToggleReviewRow(ImportReviewRow row)
    {
        if (IsReviewWorkspaceEnabled) _reviewWorkspace.Toggle(row.Items);
    }

    public void ToggleReviewSection(ImportReviewRow row)
    {
        if (IsReviewWorkspaceEnabled && row.Kind == ImportReviewRowKind.Section)
            _reviewWorkspace.ToggleSectionOnly(row.Items);
    }

    public void ToggleReviewGroup(ImportReviewGroup group)
    {
        if (IsReviewWorkspaceEnabled) _reviewWorkspace.Toggle(group);
    }

    [RelayCommand]
    private async Task FetchReviewArtworkAsync()
    {
        await WaitForLinkMatchAsync();
        await ApplyReviewChangesAsync(true, false, false);
    }
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
            _reviewWorkspace.SetSelection(_ => false); // one recount, not one per selected card
            if (applyAuthor) { ReviewAuthorProviderId = null; ReviewAuthorId = null; }
            if (applyArtwork)
            {
                _keepLinkMatchOnClear = true;
                try { ReviewArtworkProviderId = null; ReviewArtworkId = null; }
                finally { _keepLinkMatchOnClear = false; }
            }
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
        _pendingUnknownItems.Remove(item);
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

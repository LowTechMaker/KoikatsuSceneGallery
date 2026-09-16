using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>
/// Drives the local collection page: files are staged first, then assigned to
/// a local source in one gesture, which imports them.
/// </summary>
/// <remarks>
/// Separate from <see cref="ImportViewModel"/> rather than a mode on it. Both
/// pages are cached, so a shared item collection would have them fighting over
/// it, and this flow has no review step to share: dropping on a source is the
/// commit.
///
/// Staging cannot run analysis. Analysis needs to know the owner before it
/// reads anything — that is how "no outbound lookups for local cards" is
/// enforced — but here the owner is not chosen until the drop. So staging
/// classifies only, and analysis runs at assignment time with the source
/// already known.
/// </remarks>
public sealed partial class LocalImportViewModel : ObservableObject
{
    /// <summary>
    /// How many staged files get a thumbnail. Caps the memory the strip holds
    /// and the number of visuals the flight animation touches, whatever the
    /// batch size; the count below the strip still reports every file.
    /// </summary>
    public const int MaxStripThumbnails = 24;

    /// <summary>
    /// How long to wait before rebuilding the tiles after a change.
    /// </summary>
    /// <remarks>
    /// AuthorInfoService raises AuthorsChanged once per card as a gallery
    /// loads, so an unthrottled handler here queues one full rebuild per card
    /// — tens of thousands of them on a real library, which freezes the UI.
    /// Matches the debounce AuthorsViewModel uses for the same event.
    /// </remarks>
    private static readonly TimeSpan TileRebuildDebounce = TimeSpan.FromMilliseconds(500);

    private readonly ImportService _importService;
    private readonly ImportExecutionCoordinator _executionCoordinator;
    private readonly LocalSourceRegistry _localSourceRegistry;
    private readonly AuthorInfoService _authorInfoService;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly Func<Action, bool> _publish;
    private readonly DispatcherQueue? _dispatcher;
    private readonly Func<string, string> _getString;
    private readonly IAppLogger _logger;

    private readonly ObservableCollection<ImportItem> _items = [];
    private readonly DispatcherQueueTimer? _tileRebuildTimer;
    private CancellationTokenSource? _staging;
    private string? _tilesDescription;

    internal LocalImportViewModel(
        ImportService importService,
        ImportExecutionCoordinator executionCoordinator,
        LocalSourceRegistry localSourceRegistry,
        AuthorInfoService authorInfoService,
        ThumbnailCacheService thumbnailCacheService,
        DispatcherQueue dispatcher,
        IAppLogger logger)
        : this(importService, executionCoordinator, localSourceRegistry, authorInfoService,
            thumbnailCacheService, action => dispatcher.TryEnqueue(() => action()), dispatcher,
            UiText.Get, logger)
    {
    }

    internal LocalImportViewModel(
        ImportService importService,
        ImportExecutionCoordinator executionCoordinator,
        LocalSourceRegistry localSourceRegistry,
        AuthorInfoService authorInfoService,
        ThumbnailCacheService thumbnailCacheService,
        Func<Action, bool> publish,
        DispatcherQueue? dispatcher,
        Func<string, string> getString,
        IAppLogger logger)
    {
        _importService = importService;
        _executionCoordinator = executionCoordinator;
        _localSourceRegistry = localSourceRegistry;
        _authorInfoService = authorInfoService;
        _thumbnailCacheService = thumbnailCacheService;
        _publish = publish;
        _dispatcher = dispatcher;
        _getString = getString;
        _logger = logger;

        if (dispatcher is not null)
        {
            _tileRebuildTimer = dispatcher.CreateTimer();
            _tileRebuildTimer.Interval = TileRebuildDebounce;
            _tileRebuildTimer.IsRepeating = false;
            _tileRebuildTimer.Tick += (_, _) => RefreshTiles();
        }

        _localSourceRegistry.SourcesChanged += OnSourcesChanged;
        _authorInfoService.AuthorsChanged += OnSourcesChanged;
        RefreshTiles();
    }

    /// <summary>Every staged file.</summary>
    public ObservableCollection<StagedCard> StagedCards { get; } = [];

    /// <summary>The prefix of <see cref="StagedCards"/> the strip shows.</summary>
    public ObservableCollection<StagedCard> VisibleStagedCards { get; } = [];

    /// <summary>Local sources plus the trailing add cell.</summary>
    public ObservableCollection<object> Tiles { get; } = [];

    /// <summary>
    /// Identifies the current staged batch inside a drag package, so a drop
    /// handler can tell our folder from files dragged in from outside.
    /// </summary>
    public string StagedBatchToken { get; private set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStagedCards))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(PendingCountText))]
    [NotifyPropertyChangedFor(nameof(CanAssign))]
    public partial int PendingCount { get; private set; }

    /// <summary>Files dropped in that turned out not to be cards.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRejected))]
    [NotifyPropertyChangedFor(nameof(RejectedText))]
    public partial int RejectedCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHiddenThumbnails))]
    [NotifyPropertyChangedFor(nameof(HiddenThumbnailText))]
    public partial int HiddenThumbnailCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssign))]
    public partial bool IsStaging { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssign))]
    public partial bool IsImporting { get; private set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; } = "";

    public bool HasStagedCards => PendingCount > 0;

    public bool IsEmpty => PendingCount == 0;

    public bool HasRejected => RejectedCount > 0;

    public bool HasHiddenThumbnails => HiddenThumbnailCount > 0;

    /// <summary>False while there is nothing to assign, or an operation owns the batch.</summary>
    public bool CanAssign => HasStagedCards && !IsStaging && !IsImporting;

    public string PendingCountText => Format("LocalSources_PendingCount", PendingCount);

    public string RejectedText => Format("LocalSources_RejectedCount", RejectedCount);

    public string HiddenThumbnailText => Format("LocalSources_MoreThumbnails", HiddenThumbnailCount);

    private string Format(string key, params object[] args) => string.Format(_getString(key), args);

    /// <summary>Raised after a transaction commits, with the files it landed.</summary>
    /// <remarks>
    /// Do not force a gallery reload from here. Each card service watches its
    /// library roots with IncludeSubdirectories, so the moved files arrive in
    /// the galleries on their own, one card at a time, and the author counts
    /// follow from that. Reloading instead rescans the whole library — tens of
    /// thousands of cards — and freezes the app for the sake of information it
    /// was about to get for free.
    /// </remarks>
    public event Action<IReadOnlyList<string>>? ImportCommitted;

    /// <summary>
    /// Raised after an import that left files behind because the library
    /// already held identical cards, with those source paths.
    /// </summary>
    /// <remarks>
    /// Separate from the status line because the user has to be able to act on
    /// it: the files are still in their original folder, and finding them
    /// again is the whole point of reporting them.
    /// </remarks>
    public event Action<IReadOnlyList<string>>? DuplicatesKept;

    /// <summary>
    /// Classifies dropped files and adds the cards among them to the batch.
    /// </summary>
    /// <remarks>
    /// Classification here, rather than at assignment time, is what makes the
    /// pending count honest: a strip showing files that will be rejected is a
    /// lie the user only discovers after committing. It reads the tail of each
    /// PNG without decoding it, and analysis repeats the read later from the
    /// warm file cache.
    /// </remarks>
    public async Task StageAsync(IReadOnlyList<string> filePaths)
    {
        if (filePaths.Count == 0 || IsImporting)
            return;

        var known = new HashSet<string>(
            StagedCards.Select(card => card.FilePath),
            StringComparer.OrdinalIgnoreCase);
        var candidates = filePaths
            .Where(path => Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            .Where(known.Add)
            .ToArray();
        if (candidates.Length == 0)
            return;

        _staging?.Cancel();
        _staging?.Dispose();
        _staging = new CancellationTokenSource();
        var token = _staging.Token;

        IsStaging = true;
        try
        {
            var (staged, rejected) = await Task.Run(() =>
            {
                var accepted = new List<StagedCard>(candidates.Length);
                var skipped = 0;
                foreach (var path in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    var (cardType, gameVersion) = CardTypeClassifier.ClassifyExtended(path);
                    if (cardType == CardType.NotACard)
                    {
                        skipped++;
                        continue;
                    }

                    accepted.Add(new StagedCard
                    {
                        FilePath = path,
                        CardType = cardType,
                        GameVersion = gameVersion,
                        DateModified = SafeLastWriteTime(path),
                    });
                }

                return (accepted, skipped);
            }, token).ConfigureAwait(true);

            foreach (var card in staged)
                StagedCards.Add(card);
            RejectedCount += rejected;
            SyncVisibleStagedCards();
            await LoadThumbnailsAsync(token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer drop, or cleared.
        }
        catch (Exception ex)
        {
            _logger.LogError("LocalImport.Stage", ex);
        }
        finally
        {
            IsStaging = false;
        }
    }

    /// <summary>Drops the whole batch without importing it.</summary>
    public void ClearStaging()
    {
        _staging?.Cancel();
        StagedCards.Clear();
        VisibleStagedCards.Clear();
        PendingCount = 0;
        RejectedCount = 0;
        HiddenThumbnailCount = 0;
        StatusText = "";
        StagedBatchToken = Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// Forgets staged files that have gone from disk. Called when the page is
    /// revisited, since it is cached and a batch can outlive the files.
    /// </summary>
    public void RevalidateStaging()
    {
        for (var i = StagedCards.Count - 1; i >= 0; i--)
        {
            if (!File.Exists(StagedCards[i].FilePath))
                StagedCards.RemoveAt(i);
        }

        SyncVisibleStagedCards();
    }

    /// <summary>Registers a source the user just named, before it has a folder.</summary>
    public LocalSourceEntry CreatePendingSource(string displayName)
        => _localSourceRegistry.CreatePending(displayName);

    /// <summary>
    /// Imports the staged batch into one local source. Analysis runs here,
    /// with the source already known, so nothing about these files is ever
    /// looked up.
    /// </summary>
    public async Task ImportStagedToAsync(LocalSourceEntry target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!CanAssign)
            return;

        var paths = StagedCards.Select(card => card.FilePath).ToArray();
        IsImporting = true;
        StatusText = Format("LocalSources_ImportingTo", target.DisplayName);

        try
        {
            _items.Clear();
            await _importService.AnalyzeAsync(
                paths,
                _items,
                _publish,
                CancellationToken.None,
                new ImportAnalysisOptions(new LocalSourceAssignment(target.Id, target.DisplayName)))
                .ConfigureAwait(true);

            // Cards the library already holds are dropped from the plan, so
            // their files are never touched — which is right, but silent. They
            // belong in the same report as the duplicates the transaction
            // finds, because to the user they are the same thing.
            var alreadyInLibrary = _items
                .Where(item => item.Status == ImportItemStatus.AlreadyInLibrary)
                .Select(item => item.SourceFilePath)
                .ToArray();

            var eligible = _items
                .Where(item => ImportReviewPolicy.CanExecute(item.Status, item.DestinationPath))
                .Select(item => (item.SourceFilePath, Destination: item.DestinationPath!))
                .ToArray();
            if (eligible.Length == 0)
            {
                StatusText = DescribeNothingEligible();
                // The batch is deliberately kept here: nothing was imported,
                // so there is nothing to consume.
                if (alreadyInLibrary.Length > 0)
                    DuplicatesKept?.Invoke(alreadyInLibrary);

                return;
            }

            // Off the UI thread: resolving a name clash compares file contents.
            var plans = await Task.Run(() => BuildPlans(eligible)).ConfigureAwait(true);

            // Collected before the batch is cleared, and derived from the
            // resolved destinations rather than the items' author directory,
            // which destination resolution leaves unset for a local card.
            var targets = LocalSourceTargets.Collect(_items);
            var result = await _executionCoordinator
                .ExecuteAsync(plans, _ => { })
                .ConfigureAwait(true);

            if (!result.Receipt.IsFullySuccessful)
            {
                LogReceipt(result);
                StatusText = LocalImportFailureText.Describe(
                    result.Receipt.ItemReceipts,
                    paths.Length,
                    _getString);
                return;
            }

            _importService.RegisterCommittedLibraryFiles(result.CommittedPaths);
            foreach (var (directory, id, displayName) in targets)
            {
                await _localSourceRegistry
                    .EnsureDocumentAsync(directory, id, displayName)
                    .ConfigureAwait(true);
            }

            _localSourceRegistry.BeginRescan();
            var committedCount = result.CommittedPaths.Count;
            var duplicates = Duplicates(alreadyInLibrary, result.DuplicatePaths);
            ClearStaging();
            StatusText = duplicates.Count == 0
                ? Format("LocalSources_ImportSucceeded", committedCount, target.DisplayName)
                : Format(
                    "LocalSources_ImportSucceededWithDuplicates",
                    committedCount,
                    target.DisplayName,
                    duplicates.Count);
            ImportCommitted?.Invoke(result.CommittedPaths);
            if (duplicates.Count > 0)
                DuplicatesKept?.Invoke(duplicates);
        }
        catch (InvalidOperationException ex)
        {
            // The execution coordinator allows one transaction at a time, and
            // the online import page shares it, so the blocker may not be us.
            _logger.LogError("LocalImport.Busy", ex, target.Id);
            StatusText = _getString("LocalSources_ImportBusy");
        }
        catch (Exception ex)
        {
            _logger.LogError("LocalImport.Import", ex, target.Id);
            StatusText = Format("LocalSources_ImportFailed", paths.Length);
        }
        finally
        {
            _items.Clear();
            IsImporting = false;
        }
    }

    /// <summary>
    /// Explains why a batch produced no executable plan, and records the
    /// per-item reasons.
    /// </summary>
    /// <remarks>
    /// Worth the detail: every reason here is invisible from the UI otherwise,
    /// and "already in the library" is both the most likely one and the one a
    /// generic message explains worst.
    /// </remarks>
    private string DescribeNothingEligible()
    {
        var breakdown = string.Join(
            ", ",
            _items
                .GroupBy(item => item.Status)
                .Select(group => $"{group.Key}={group.Count()}"));
        var missingDestination = _items.Count(item => string.IsNullOrWhiteSpace(item.DestinationPath));
        _logger.LogError(
            "LocalImport.NothingEligible",
            new InvalidOperationException(
                $"items={_items.Count} {breakdown} missingDestination={missingDestination}"),
            _items.FirstOrDefault()?.SourceFilePath);

        if (_items.Count > 0 && _items.All(item => item.Status == ImportItemStatus.AlreadyInLibrary))
            return _getString("LocalSources_ImportAlreadyInLibrary");

        return missingDestination == _items.Count && _items.Count > 0
            ? _getString("LocalSources_ImportNoDestination")
            : _getString("LocalSources_ImportNothingEligible");
    }

    /// <summary>Records what a failed transaction actually did, per item.</summary>
    private void LogReceipt(ImportExecutionResult result)
    {
        var failures = string.Join(
            ", ",
            result.Receipt.ItemReceipts
                .Where(item => item.FailureType != TransactionFailureType.None)
                .Select(item => $"{Path.GetFileName(item.SourceFilePath)}:{item.FailureType}"
                                + (item.RequiresManualRecovery ? "(manual)" : "")));
        _logger.LogError(
            "LocalImport.TransactionFailed",
            new InvalidOperationException(
                $"failed={result.FailedCount} rolledBack={result.SafelyRolledBackCount} "
                + $"manual={result.ManualRecoveryCount} warnings={result.WarningCount} [{failures}]"),
            result.Receipt.ItemReceipts.FirstOrDefault()?.TargetFilePath);
    }

    /// <summary>
    /// Turns the eligible items into plans, giving each a destination no other
    /// card in the batch — or in the library — has already taken with different
    /// content.
    /// </summary>
    /// <remarks>
    /// Reads file contents, so it runs on a background thread over a snapshot
    /// rather than over the observable collection.
    ///
    /// A local card has no remote artwork, so it never carries a sidecar
    /// document; the source description is written per folder instead.
    /// </remarks>
    private static List<ImportItemPlan> BuildPlans(
        IReadOnlyList<(string SourceFilePath, string Destination)> eligible)
    {
        var claimed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<ImportItemPlan>(eligible.Count);
        foreach (var (source, destination) in eligible)
        {
            plans.Add(new ImportItemPlan(
                source,
                LocalDestinationNames.Resolve(
                    source,
                    destination,
                    claimed,
                    File.Exists,
                    HaveSameContent),
                AuthorDirectoryPath: null,
                Document: null)
            {
                // A local batch is read out of a folder the user keeps, not a
                // download folder to be emptied, so a card the library already
                // has stays where it is and gets reported instead.
                KeepDuplicateSource = true,
            });
        }

        return plans;
    }

    /// <summary>
    /// Every source file the import left where it was because the library
    /// already had that card, from both places that can decide so.
    /// </summary>
    private static IReadOnlyList<string> Duplicates(
        IReadOnlyList<string> alreadyInLibrary,
        IReadOnlyList<string> foundByTheTransaction)
        => [.. alreadyInLibrary
            .Concat(foundByTheTransaction)
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Whether two files hold the same bytes. An unreadable file counts as
    /// different, which suffixes the name instead of folding a card away on the
    /// strength of a failed read.
    /// </summary>
    private static bool HaveSameContent(string left, string right)
    {
        try
        {
            return ImportDuplicateDetector.AreFilesIdentical(left, right);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void SyncVisibleStagedCards()
    {
        PendingCount = StagedCards.Count;
        HiddenThumbnailCount = Math.Max(0, StagedCards.Count - MaxStripThumbnails);

        var visible = Math.Min(StagedCards.Count, MaxStripThumbnails);
        while (VisibleStagedCards.Count > visible)
            VisibleStagedCards.RemoveAt(VisibleStagedCards.Count - 1);
        for (var i = 0; i < visible; i++)
        {
            if (i < VisibleStagedCards.Count)
            {
                if (!ReferenceEquals(VisibleStagedCards[i], StagedCards[i]))
                    VisibleStagedCards[i] = StagedCards[i];
            }
            else
            {
                VisibleStagedCards.Add(StagedCards[i]);
            }
        }
    }

    private async Task LoadThumbnailsAsync(CancellationToken cancellationToken)
    {
        foreach (var card in VisibleStagedCards.ToArray())
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            if (card.ThumbnailPath is not null)
                continue;

            // Path+date keyed, so it works on files that are not in the
            // library yet. Binding the source PNG instead would decode each
            // card at full size.
            card.ThumbnailPath = await _thumbnailCacheService
                .EnsureThumbnailAsync(card.FilePath, card.DateModified, cancellationToken)
                .ConfigureAwait(true);
        }
    }

    private void OnSourcesChanged()
    {
        // Coalesced: this fires once per card assignment during a gallery load.
        if (_tileRebuildTimer is null)
        {
            RefreshTiles();
            return;
        }

        _tileRebuildTimer.Stop();
        _tileRebuildTimer.Start();
    }

    private void RefreshTiles()
    {
        var tiles = LocalSourceTileBuilder.Build(
            _localSourceRegistry.Sources,
            _authorInfoService.GetSummaries());

        // Replacing the collection tears down every container, and each one
        // holds a live tile that restarts its own cross-fade. Skip that when
        // nothing a tile shows has actually changed.
        if (DescribeTiles(tiles) == _tilesDescription)
            return;

        _tilesDescription = DescribeTiles(tiles);
        Tiles.Clear();
        foreach (var tile in tiles)
            Tiles.Add(tile);
        Tiles.Add(AddLocalSourceTile.Instance);
    }

    private static string DescribeTiles(IReadOnlyList<LocalSourceTile> tiles)
        => string.Join(
            "",
            tiles.Select(tile =>
                $"{tile.Entry.Id}|{tile.Summary.Display.Name}|{tile.Summary.TotalCount}|{tile.Entry.AvatarPath}"));

    private static DateTime SafeLastWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTime(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}

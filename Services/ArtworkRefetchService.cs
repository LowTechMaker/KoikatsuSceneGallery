using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

public enum ArtworkRefetchPhase
{
    Idle,
    Scanning,
    Scanned,
    BackingUp,
    Running,
    Restoring,
    Stopped,
    Completed,
}

public enum ArtworkRefetchStopReason
{
    None,
    UserStopped,
    TooManyFailures,
    BackupFailed,
    Error,
}

/// <summary>What the last restore put back, for the page to report.</summary>
public sealed record ArtworkRestoreReport(DateTimeOffset BackupCreatedAt, int Restored, int Removed, int Failed);

/// <summary>
/// Fetches every already-imported artwork of one platform again and rewrites
/// its sidecars — for cards imported before the importer saved their data, and
/// for data saved in an older form (English tag translations, missing titles).
/// </summary>
/// <remarks>
/// Runs for hours under the plugin's rate limit, so it lives beyond the dialog
/// that starts it and reports through observable state the whole app can show.
/// One run at a time: the plugin's limiter is shared, and two runs would only
/// queue behind each other while doubling what the platform sees.
///
/// The posts come from <see cref="AuthorPostService"/>, the same scan behind
/// the author pages, so the run touches exactly the artworks those pages show
/// and names the same files in each sidecar.
/// </remarks>
public sealed partial class ArtworkRefetchService : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AuthorPostService _posts;
    private readonly SidecarBackupStore _backups;
    private readonly DispatcherQueue _dispatcher;
    private readonly IAppLogger _logger;
    private readonly Action _onCompleted;
    private readonly string _statePath;
    private readonly string _gonePath;
    private readonly object _gate = new();
    private Dictionary<string, RunRecord> _runs;
    // Artworks the platform has said are not there, per platform. Kept across
    // runs: a deleted artwork never gets data, so without this every
    // missing-only run — and every resume — asked about the same ones first,
    // and the first real run stopped itself on a streak of them.
    private Dictionary<string, HashSet<string>> _gone;
    private CancellationTokenSource? _cts;
    private AuthorPostScanResult? _scan;
    // Which kind of run the scan was counted for. Kept here, not in the
    // dialog: a scan made in one opening of it is started from the next,
    // and the summary it showed ("N will be skipped") must hold then too.
    private bool _scanResume;

    public ArtworkRefetchService(
        AuthorPostService posts,
        DispatcherQueue dispatcher,
        IAppLogger logger,
        Action onCompleted,
        string? statePath = null,
        string? backupRoot = null)
    {
        _posts = posts;
        _backups = new SidecarBackupStore(backupRoot ?? Path.Combine(AppPaths.LocalFolder, "refetch-backups"));
        _dispatcher = dispatcher;
        _logger = logger;
        _onCompleted = onCompleted;
        _statePath = statePath ?? Path.Combine(AppPaths.LocalFolder, "artwork-refetch.json");
        _gonePath = Path.Combine(Path.GetDirectoryName(_statePath)!, "artwork-refetch-gone.json");
        _runs = LoadRuns();
        _gone = LoadGone();
    }

    [ObservableProperty]
    public partial ArtworkRefetchPhase Phase { get; private set; }

    [ObservableProperty]
    public partial ArtworkRefetchStopReason StopReason { get; private set; }

    /// <summary>The platform the current or last run is for.</summary>
    [ObservableProperty]
    public partial string? ProviderId { get; private set; }

    [ObservableProperty]
    public partial int Total { get; private set; }

    [ObservableProperty]
    public partial int CardCount { get; private set; }

    /// <summary>Whether the last scan was counted for resuming the unfinished run.</summary>
    [ObservableProperty]
    public partial bool ScannedForResume { get; private set; }

    /// <summary>Artworks with no saved data at all — what a missing-only run fetches.</summary>
    [ObservableProperty]
    public partial int Missing { get; private set; }

    /// <summary>Artworks a resumed run skips because it already fetched them.</summary>
    [ObservableProperty]
    public partial int Skipped { get; private set; }

    [ObservableProperty]
    public partial int Processed { get; private set; }

    [ObservableProperty]
    public partial int Updated { get; private set; }

    /// <summary>Artworks this run found deleted or private.</summary>
    [ObservableProperty]
    public partial int Gone { get; private set; }

    /// <summary>Artworks this run could not get an answer for; they are tried again next time.</summary>
    [ObservableProperty]
    public partial int Failed { get; private set; }

    /// <summary>Artworks an earlier run found deleted or private, which the scan leaves out.</summary>
    [ObservableProperty]
    public partial int KnownGone { get; private set; }

    /// <summary>Cards whose artwork could not be told from folder or filename.</summary>
    [ObservableProperty]
    public partial int Unassigned { get; private set; }

    /// <summary>The last restore, until the next scan or run replaces it.</summary>
    [ObservableProperty]
    public partial ArtworkRestoreReport? LastRestore { get; private set; }

    /// <summary>Artworks this run fetches, excluding what it skips.</summary>
    /// <remarks>
    /// What progress is measured against. Counting skipped artworks as done
    /// made a missing-only run that had fetched six read "2488 / 10941".
    /// </remarks>
    public int RunTotal => Math.Max(0, Total - Skipped);

    /// <summary>Artworks this run has fetched or given up on so far.</summary>
    public int RunDone => Math.Max(0, Processed - Skipped);

    partial void OnTotalChanged(int value) => OnPropertyChanged(nameof(RunTotal));

    partial void OnSkippedChanged(int value)
    {
        OnPropertyChanged(nameof(RunTotal));
        OnPropertyChanged(nameof(RunDone));
    }

    partial void OnProcessedChanged(int value) => OnPropertyChanged(nameof(RunDone));

    public bool IsBusy => Phase is ArtworkRefetchPhase.Scanning or ArtworkRefetchPhase.BackingUp
        or ArtworkRefetchPhase.Running or ArtworkRefetchPhase.Restoring;

    partial void OnPhaseChanged(ArtworkRefetchPhase value) => OnPropertyChanged(nameof(IsBusy));

    /// <summary>Whether this platform can be re-fetched at all.</summary>
    public bool IsAvailableFor(string providerId) => _posts.CanRefreshArtworks(providerId);

    /// <summary>
    /// Whether a run for <paramref name="providerId"/> was started and never
    /// finished — the case "continue where it stopped" is for.
    /// </summary>
    public bool HasUnfinishedRun(string providerId)
    {
        lock (_gate)
            return _runs.TryGetValue(providerId, out var run) && run.CompletedAt is null;
    }

    /// <summary>
    /// Which artworks the unfinished run covers, or null when there is none.
    /// A resumed run keeps the choice its start made.
    /// </summary>
    public ArtworkRefetchScope? UnfinishedScope(string providerId)
    {
        lock (_gate)
            return _runs.TryGetValue(providerId, out var run) && run.CompletedAt is null ? run.Scope : null;
    }

    /// <summary>
    /// Finds the platform's artworks without fetching anything, so the user
    /// sees how many there are, and how long it will take, before agreeing.
    /// </summary>
    public async Task ScanAsync(string providerId, bool resume)
    {
        if (!TryBegin(providerId, ArtworkRefetchPhase.Scanning, out var cts)) return;

        try
        {
            var scan = await _posts.ScanProviderPostDataAsync(providerId, cts.Token).ConfigureAwait(false);
            var resumeFrom = resume ? RunStartedAt(providerId) : null;
            var scope = resume ? UnfinishedScope(providerId) ?? ArtworkRefetchScope.All : ArtworkRefetchScope.All;
            var gone = GoneSnapshot(providerId);
            bool IsGone(AuthorPost post) => gone.Contains(post.ArtworkId.Id);
            var knownGone = scan.Posts.Count(IsGone);
            var skipped = resume
                ? scan.Posts.Count(post => IsGone(post)
                    || !ArtworkRefetchPolicy.NeedsFetch(post.MetadataFetchedAt, resumeFrom, scope))
                : knownGone;
            var missing = scan.Posts.Count(post => post.MetadataFetchedAt is null && !IsGone(post));
            _scan = scan;
            _scanResume = resume;
            Publish(() =>
            {
                Total = scan.Posts.Count;
                CardCount = scan.Posts.Sum(post => post.LocalFileCount);
                Skipped = skipped;
                Missing = missing;
                KnownGone = knownGone;
                ScannedForResume = resume;
                Unassigned = scan.UnassignedImages.Count;
                Phase = ArtworkRefetchPhase.Scanned;
            });
        }
        catch (OperationCanceledException)
        {
            Publish(() => Phase = ArtworkRefetchPhase.Idle);
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.Scan", ex, providerId);
            Publish(() => { StopReason = ArtworkRefetchStopReason.Error; Phase = ArtworkRefetchPhase.Stopped; });
        }
        finally
        {
            End(cts);
        }
    }

    /// <summary>
    /// Runs the re-fetch over the last scan, resuming or starting over as that
    /// scan was made for. Returns once the run is under way; progress arrives
    /// through the observable state.
    /// </summary>
    /// <param name="scope">
    /// Which artworks a run from the start covers. Ignored when resuming: the
    /// run goes on covering what it was started for.
    /// </param>
    public void Start(string providerId, ArtworkRefetchScope scope = ArtworkRefetchScope.All)
    {
        var scan = _scan;
        var resume = _scanResume;
        if (scan is null || !string.Equals(ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) return;
        if (!TryBegin(providerId, ArtworkRefetchPhase.BackingUp, out var cts)) return;

        DateTimeOffset? resumeFrom;
        lock (_gate)
        {
            if (resume && _runs.TryGetValue(providerId, out var run) && run.CompletedAt is null)
            {
                resumeFrom = run.StartedAt;
                scope = run.Scope;
            }
            else
            {
                resumeFrom = null;
                // Recorded before the first request, so a run cut off by closing
                // the app can still be resumed from the next launch.
                _runs[providerId] = new RunRecord(DateTimeOffset.UtcNow, null, BackupId: null, scope);
            }
        }
        SaveRuns();

        var gone = GoneSnapshot(providerId);
        var pending = scan.Posts
            .Where(post => !gone.Contains(post.ArtworkId.Id)
                && ArtworkRefetchPolicy.NeedsFetch(post.MetadataFetchedAt, resumeFrom, scope))
            .ToList();
        var skipped = scan.Posts.Count - pending.Count;
        Publish(() =>
        {
            Total = scan.Posts.Count;
            Skipped = skipped;
            Processed = skipped;
            Updated = 0;
            Gone = 0;
            Failed = 0;
        });

        _ = Task.Run(() => RunAsync(providerId, pending, cts));
    }

    /// <summary>The platform's backups taken before earlier runs, newest first.</summary>
    internal IReadOnlyList<SidecarBackup> Backups(string providerId)
    {
        try
        {
            return _backups.List(providerId);
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.ListBackups", ex, providerId);
            return [];
        }
    }

    /// <summary>
    /// Puts the sidecars back as they were before the run that took
    /// <paramref name="backup"/>, and forgets that run: with its writes undone
    /// there is nothing left to continue.
    /// </summary>
    /// <remarks>
    /// Not cancellable. A restore stopped half way would leave some sidecars
    /// from before the run and some from after it — worse than either.
    /// </remarks>
    internal async Task RestoreAsync(SidecarBackup backup)
    {
        if (!TryBegin(backup.ProviderId, ArtworkRefetchPhase.Restoring, out var cts)) return;
        try
        {
            var result = await Task.Run(() => _backups.RestoreAsync(backup, CancellationToken.None)).ConfigureAwait(false);
            lock (_gate)
                _runs.Remove(backup.ProviderId);
            SaveRuns();
            _scan = null;
            Publish(() =>
            {
                LastRestore = new ArtworkRestoreReport(backup.CreatedAt, result.Restored, result.Removed, result.Failed);
                Phase = ArtworkRefetchPhase.Idle;
            });
            Publish(_onCompleted);
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.Restore", ex, backup.Id);
            Publish(() => { StopReason = ArtworkRefetchStopReason.Error; Phase = ArtworkRefetchPhase.Stopped; });
        }
        finally
        {
            End(cts);
        }
    }

    /// <summary>Stops the scan or run in progress; a stopped run can be resumed.</summary>
    public void Stop()
    {
        lock (_gate)
            _cts?.Cancel();
    }

    private async Task RunAsync(string providerId, IReadOnlyList<AuthorPost> pending, CancellationTokenSource cts)
    {
        var consecutiveFailures = 0;
        var updated = 0;
        var gone = 0;
        var failed = 0;
        try
        {
            if (!await BackUpAsync(providerId, pending, cts.Token).ConfigureAwait(false))
                return;
            Publish(() => Phase = ArtworkRefetchPhase.Running);

            foreach (var post in pending)
            {
                cts.Token.ThrowIfCancellationRequested();

                ArtworkRefreshStatus status;
                try
                {
                    status = await _posts.RefreshArtworkDetailAsync(post, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError("ArtworkRefetch.Artwork", ex, post.ArtworkId.Id);
                    status = ArtworkRefreshStatus.Failed;
                }

                // Only a request that got no answer counts towards stopping. A
                // deleted artwork is an answer — it shows the connection works.
                switch (status)
                {
                    case ArtworkRefreshStatus.Found:
                        updated++;
                        consecutiveFailures = 0;
                        // A run lasts hours; what it has written so far should
                        // not wait for the end to reach the tag cloud.
                        if (updated % ArtworkRefetchPolicy.TagCloudRefreshEvery == 0)
                            Publish(_onCompleted);
                        break;
                    case ArtworkRefreshStatus.Gone:
                        gone++;
                        consecutiveFailures = 0;
                        MarkGone(providerId, post.ArtworkId.Id, save: gone % 25 == 0);
                        break;
                    default:
                        failed++;
                        consecutiveFailures++;
                        break;
                }

                var (u, g, f) = (updated, gone, failed);
                Publish(() => { Processed++; Updated = u; Gone = g; Failed = f; });

                if (consecutiveFailures >= ArtworkRefetchPolicy.MaxConsecutiveFailures)
                {
                    Publish(() => { StopReason = ArtworkRefetchStopReason.TooManyFailures; Phase = ArtworkRefetchPhase.Stopped; });
                    return;
                }
            }

            lock (_gate)
            {
                if (_runs.TryGetValue(providerId, out var run))
                    _runs[providerId] = run with { CompletedAt = DateTimeOffset.UtcNow };
            }
            SaveRuns();
            _scan = null;
            Publish(() => Phase = ArtworkRefetchPhase.Completed);
        }
        catch (OperationCanceledException)
        {
            Publish(() => { StopReason = ArtworkRefetchStopReason.UserStopped; Phase = ArtworkRefetchPhase.Stopped; });
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.Run", ex, providerId);
            Publish(() => { StopReason = ArtworkRefetchStopReason.Error; Phase = ArtworkRefetchPhase.Stopped; });
        }
        finally
        {
            if (gone > 0) SaveGone();
            End(cts);
            // Whatever was written is on disk now, finished or not; the pages
            // that read sidecars are told either way.
            if (updated > 0)
                Publish(_onCompleted);
        }
    }

    /// <summary>
    /// Saves every sidecar the run may write before it writes any. A run that
    /// cannot be undone does not start.
    /// </summary>
    /// <remarks>
    /// A run from the top takes a new backup. A resumed run adds to the one
    /// its start took — whatever the first leg wrote is already covered there
    /// in its earlier state, and saving it again now would save the new data.
    /// </remarks>
    private async Task<bool> BackUpAsync(string providerId, IReadOnlyList<AuthorPost> pending, CancellationToken ct)
    {
        try
        {
            var paths = pending.SelectMany(_posts.SidecarPathsFor).ToList();
            string? backupId;
            lock (_gate)
                backupId = _runs.TryGetValue(providerId, out var run) ? run.BackupId : null;

            var existing = backupId is null
                ? null
                : _backups.List(providerId).FirstOrDefault(b => b.Id == backupId);
            var backup = existing is null
                ? await _backups.CreateAsync(providerId, paths, DateTimeOffset.UtcNow, ct).ConfigureAwait(false)
                : await _backups.ExtendAsync(existing, paths, ct).ConfigureAwait(false);

            lock (_gate)
            {
                if (_runs.TryGetValue(providerId, out var run))
                    _runs[providerId] = run with { BackupId = backup.Id };
            }
            SaveRuns();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.Backup", ex, providerId);
            Publish(() => { StopReason = ArtworkRefetchStopReason.BackupFailed; Phase = ArtworkRefetchPhase.Stopped; });
            return false;
        }
    }

    private bool TryBegin(string providerId, ArtworkRefetchPhase phase, out CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (_cts is not null)
            {
                cts = null!;
                return false;
            }
            cts = _cts = new CancellationTokenSource();
        }
        Publish(() =>
        {
            ProviderId = providerId;
            StopReason = ArtworkRefetchStopReason.None;
            LastRestore = null;
            Phase = phase;
        });
        return true;
    }

    private void End(CancellationTokenSource cts)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
        }
        cts.Dispose();
    }

    private DateTimeOffset? RunStartedAt(string providerId)
    {
        lock (_gate)
            return _runs.TryGetValue(providerId, out var run) && run.CompletedAt is null ? run.StartedAt : null;
    }

    private HashSet<string> GoneSnapshot(string providerId)
    {
        lock (_gate)
            return _gone.TryGetValue(providerId, out var ids) ? [.. ids] : [];
    }

    private void MarkGone(string providerId, string artworkId, bool save)
    {
        lock (_gate)
        {
            if (!_gone.TryGetValue(providerId, out var ids))
                _gone[providerId] = ids = new HashSet<string>(StringComparer.Ordinal);
            ids.Add(artworkId);
        }
        if (save) SaveGone();
    }

    private Dictionary<string, HashSet<string>> LoadGone()
    {
        try
        {
            if (File.Exists(_gonePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(_gonePath));
                if (loaded is not null)
                    return new(
                        loaded.Select(pair => KeyValuePair.Create(pair.Key, new HashSet<string>(pair.Value, StringComparer.Ordinal))),
                        StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            // Losing this only costs asking about deleted artworks once more.
            _logger.LogError("ArtworkRefetch.LoadGone", ex, _gonePath);
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveGone()
    {
        Dictionary<string, string[]> snapshot;
        lock (_gate)
            snapshot = _gone.ToDictionary(pair => pair.Key, pair => pair.Value.Order(StringComparer.Ordinal).ToArray());
        try
        {
            AtomicJsonFile.WriteAsync(_gonePath, snapshot, JsonOptions, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.SaveGone", ex, _gonePath);
        }
    }

    private void Publish(Action update)
    {
        if (_dispatcher.HasThreadAccess) update();
        else _dispatcher.TryEnqueue(() => update());
    }

    private Dictionary<string, RunRecord> LoadRuns()
    {
        try
        {
            if (File.Exists(_statePath))
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, RunRecord>>(File.ReadAllText(_statePath));
                if (loaded is not null)
                    return new(loaded, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            // Losing this only costs "continue": the next run starts over.
            _logger.LogError("ArtworkRefetch.LoadState", ex, _statePath);
        }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    private void SaveRuns()
    {
        Dictionary<string, RunRecord> snapshot;
        lock (_gate)
            snapshot = new(_runs, StringComparer.OrdinalIgnoreCase);
        try
        {
            AtomicJsonFile.WriteAsync(_statePath, snapshot, JsonOptions, CancellationToken.None)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError("ArtworkRefetch.SaveState", ex, _statePath);
        }
    }

    private sealed record RunRecord(
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        string? BackupId = null,
        ArtworkRefetchScope Scope = ArtworkRefetchScope.All);
}

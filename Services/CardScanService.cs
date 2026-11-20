using System.Collections.Concurrent;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

public abstract class CardScanService<TCard> : IDisposable where TCard : CardBase
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges = [];
    private readonly object _lock = new();

    public event Action<TCard>? CardAdded;
    public event Action<string>? CardRemoved;

    protected CardScanService()
    {
        _debounceTimer = new System.Timers.Timer(AppTiming.FileWatchSettle.TotalMilliseconds);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) => FlushPendingChanges();
    }

    protected abstract TCard? TryCreateCard(FileInfo info);
    protected abstract IEnumerable<FileInfo> EnumerateCardFiles(string folder);
    protected abstract void ConfigureWatcher(FileSystemWatcher watcher);

    public Task<List<TCard>> ScanFoldersAsync(
        IEnumerable<string> folderPaths,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var cards = new ConcurrentBag<TCard>();
            var options = CreateScanOptions(cancellationToken);
            ScanFolders(folderPaths, options, cards.Add);
            return cards.ToList();
        }, cancellationToken);
    }

    /// <param name="skip">
    /// True for a listed file the caller already has as a card, unchanged (its
    /// card cache matches size and write time): the file is neither opened nor
    /// sent to <paramref name="onBatch"/>, which would only find it equal to the
    /// card already there. The caller records that it was seen. Called on the
    /// scan's worker threads.
    /// </param>
    public Task ScanFoldersAsync(
        IEnumerable<string> folderPaths,
        Action<List<TCard>> onBatch,
        CancellationToken cancellationToken = default,
        int batchSize = AppLimits.ScanBatchSize,
        Func<FileInfo, bool>? skip = null)
    {
        return Task.Run(() =>
        {
            var options = CreateScanOptions(cancellationToken);
            var batchLock = new object();
            var batch = new List<TCard>(batchSize);

            void Accumulate(TCard card)
            {
                List<TCard>? ready = null;
                lock (batchLock)
                {
                    batch.Add(card);
                    if (batch.Count >= batchSize)
                    {
                        ready = batch;
                        batch = new List<TCard>(batchSize);
                    }
                }
                if (ready != null)
                    onBatch(ready);
            }

            ScanFolders(folderPaths, options, Accumulate, skip);

            if (batch.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onBatch(batch);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// How many files the last scan found but could not turn into a card. A
    /// gallery holds one card per readable file, so these are the files its
    /// count is expected to fall short by.
    /// </summary>
    public int LastScanUnreadableCount => Volatile.Read(ref _lastScanUnreadableCount);
    private int _lastScanUnreadableCount;

    /// <summary>Files listed, opened, and skipped as unchanged (taken from the caller's cache) by the last scan (for the startup timing log).</summary>
    public (int Listed, int Opened, int Reused) LastScanCounts { get; private set; }

    /// <summary>
    /// Counts the card files under <paramref name="folderPaths"/> without
    /// opening any of them, or returns null when a folder cannot be listed.
    /// </summary>
    /// <remarks>
    /// Paths are de-duplicated because overlapping roots list the same file
    /// twice, while a gallery holds it once.
    /// </remarks>
    public Task<int?> CountCardFilesAsync(
        IEnumerable<string> folderPaths,
        CancellationToken cancellationToken = default)
        => Task.Run<int?>(() =>
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var folder in folderPaths)
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in EnumerateCardFiles(folder))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        files.Add(file.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return files.Count;
        }, cancellationToken);

    // Synchronous worker core: callers retain their own result collection/publication.
    private void ScanFolders(IEnumerable<string> folderPaths, ParallelOptions options, Action<TCard> accept,
        Func<FileInfo, bool>? skip = null)
    {
        var cancellationToken = options.CancellationToken;
        int unreadable = 0, listed = 0, opened = 0, reused = 0;
        foreach (var folder in folderPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder)) continue;
            Parallel.ForEach(EnumerateCardFiles(folder), options, file =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Interlocked.Increment(ref listed);
                // A file the caller already has, unchanged, is neither opened nor
                // published: its size and write time came with the listing.
                if (skip?.Invoke(file) == true)
                {
                    Interlocked.Increment(ref reused);
                    return;
                }
                Interlocked.Increment(ref opened);
                var card = TryCreateCard(file);
                if (card != null)
                    accept(card);
                else
                    Interlocked.Increment(ref unreadable);
            });
        }

        Volatile.Write(ref _lastScanUnreadableCount, unreadable);
        LastScanCounts = (listed, opened, reused);
    }

    private static ParallelOptions CreateScanOptions(CancellationToken cancellationToken)
        => new()
        {
            CancellationToken = cancellationToken,
            // Scanning opens every card and parses its header.  Unbounded use of a
            // high-core CPU competes directly with the UI thread and thumbnail work;
            // four workers retain disk parallelism without causing scroll hitching.
            MaxDegreeOfParallelism = AppLimits.IoWorkers,
        };

    private TCard? TryCreateCard(string filePath) =>
        TryCreateCard(new FileInfo(filePath));

    public void StartWatching(IEnumerable<string> folderPaths)
    {
        StopWatching();

        foreach (var folder in folderPaths)
        {
            if (!Directory.Exists(folder)) continue;

            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            };

            ConfigureWatcher(watcher);

            watcher.Created += OnFileCreated;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
            _watchers.Add(WatchDirectoryRenames(folder));
        }
    }

    /// <summary>
    /// A second watcher, for renamed folders rather than renamed files.
    /// </summary>
    /// <remarks>
    /// The card watcher filters by extension, and a folder name does not match
    /// it, so a renamed folder reaches nothing there. Renaming a local source
    /// renames its folder, which changes the path of every card inside, so
    /// missing this would leave the galleries pointing at files that no longer
    /// exist for the rest of the session.
    /// </remarks>
    private FileSystemWatcher WatchDirectoryRenames(string folder)
    {
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.DirectoryName,
        };

        watcher.Renamed += (_, e) => QueueDirectoryRename(e.OldFullPath, e.FullPath);
        watcher.EnableRaisingEvents = true;
        return watcher;
    }

    public void StopWatching()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add($"+{e.FullPath}");
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingChanges.Add($"-{e.FullPath}");
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // A renamed directory raises one event for the directory itself, not
        // one per file inside it. Left as a single change, TryCreateCard would
        // be handed a directory path and the cards under it would keep their
        // old paths for the rest of the session.
        if (Directory.Exists(e.FullPath))
        {
            QueueDirectoryRename(e.OldFullPath, e.FullPath);
            return;
        }

        lock (_lock)
        {
            _pendingChanges.Add($"-{e.OldFullPath}");
            _pendingChanges.Add($"+{e.FullPath}");
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    /// <summary>
    /// Turns a directory rename into the per-card changes the galleries
    /// understand: every card gone from the old path, and the same card
    /// arriving at the new one.
    /// </summary>
    /// <remarks>
    /// Enumerated off the watcher thread. A blocked handler is how a
    /// FileSystemWatcher overflows its buffer and starts dropping events,
    /// and a renamed folder can hold any number of cards.
    /// </remarks>
    private void QueueDirectoryRename(string oldDirectory, string newDirectory)
        => Task.Run(() =>
        {
            List<string> files;
            try
            {
                files = [.. EnumerateCardFiles(newDirectory).Select(static file => file.FullName)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }

            if (files.Count == 0)
                return;

            lock (_lock)
            {
                foreach (var file in files)
                {
                    _pendingChanges.Add($"+{file}");
                    _pendingChanges.Add(
                        $"-{Path.Combine(oldDirectory, Path.GetRelativePath(newDirectory, file))}");
                }

                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
        });

    private void FlushPendingChanges()
    {
        HashSet<string> changes;
        lock (_lock)
        {
            changes = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        foreach (var change in changes)
        {
            var path = change[1..];
            if (change[0] == '+')
            {
                var card = TryCreateCard(path);
                if (card != null)
                    CardAdded?.Invoke(card);
            }
            else
            {
                CardRemoved?.Invoke(path);
            }
        }
    }

    public void Dispose()
    {
        StopWatching();
        _debounceTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}

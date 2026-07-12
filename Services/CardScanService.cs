using System.Collections.Concurrent;
using KoikatsuSceneGallery.Models;

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
        _debounceTimer = new System.Timers.Timer(300);
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
            foreach (var folder in folderPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder)) continue;

                Parallel.ForEach(EnumerateCardFiles(folder), options, file =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var card = TryCreateCard(file);
                    if (card != null)
                        cards.Add(card);
                });
            }
            return cards.ToList();
        }, cancellationToken);
    }

    public Task ScanFoldersAsync(
        IEnumerable<string> folderPaths,
        Action<List<TCard>> onBatch,
        CancellationToken cancellationToken = default,
        int batchSize = 200)
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

            foreach (var folder in folderPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder)) continue;
                Parallel.ForEach(EnumerateCardFiles(folder), options, file =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var card = TryCreateCard(file);
                    if (card != null)
                        Accumulate(card);
                });
            }

            if (batch.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                onBatch(batch);
            }
        }, cancellationToken);
    }

    private static ParallelOptions CreateScanOptions(CancellationToken cancellationToken)
        => new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount),
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
        }
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
        lock (_lock)
        {
            _pendingChanges.Add($"-{e.OldFullPath}");
            _pendingChanges.Add($"+{e.FullPath}");
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

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

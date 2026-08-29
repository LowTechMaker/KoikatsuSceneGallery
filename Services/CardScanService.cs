using System.Collections.Concurrent;
using System.Threading.Channels;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public abstract class CardScanService<TCard> : IDisposable where TCard : CardBase
{
    private const int MaxParseAttempts = 4;
    private const int MaxScanConcurrency = 4;

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _pendingAddAttempts =
        new(StringComparer.OrdinalIgnoreCase);
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

    public TCard? TryCreateFromPath(string filePath) =>
        TryCreateCard(new FileInfo(filePath));

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

    public async Task ScanFoldersAsync(
        IEnumerable<string> folderPaths,
        Func<IReadOnlyList<TCard>, CancellationToken, Task> onBatch,
        CancellationToken cancellationToken = default,
        int batchSize = 200)
    {
        ArgumentNullException.ThrowIfNull(onBatch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        using var scanCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var scanToken = scanCancellation.Token;
        var channel = Channel.CreateBounded<TCard>(new BoundedChannelOptions(batchSize * 2)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        var producer = Task.Run(async () =>
        {
            Exception? completionError = null;
            try
            {
                var options = CreateScanOptions(scanToken);
                foreach (var folder in folderPaths)
                {
                    scanToken.ThrowIfCancellationRequested();
                    if (!Directory.Exists(folder)) continue;

                    await Parallel.ForEachAsync(
                        EnumerateCardFiles(folder),
                        options,
                        async (file, token) =>
                        {
                            token.ThrowIfCancellationRequested();
                            var card = TryCreateCard(file);
                            if (card is not null)
                                await channel.Writer.WriteAsync(card, token)
                                    .ConfigureAwait(false);
                        }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                completionError = ex;
            }
            finally
            {
                channel.Writer.TryComplete(completionError);
            }
        }, CancellationToken.None);

        try
        {
            var batch = new List<TCard>(batchSize);
            await foreach (var card in channel.Reader
                               .ReadAllAsync(scanToken)
                               .ConfigureAwait(false))
            {
                batch.Add(card);
                if (batch.Count < batchSize)
                    continue;

                var ready = batch;
                batch = new List<TCard>(batchSize);
                await onBatch(ready, scanToken).ConfigureAwait(false);
            }

            if (batch.Count > 0)
            {
                scanToken.ThrowIfCancellationRequested();
                await onBatch(batch, scanToken).ConfigureAwait(false);
            }
        }
        finally
        {
            scanCancellation.Cancel();
            await producer.ConfigureAwait(false);
        }
    }

    private static ParallelOptions CreateScanOptions(CancellationToken cancellationToken)
        => new()
        {
            CancellationToken = cancellationToken,
            // Startup can scan scenes, characters and coordinates together.
            // Giving every scan all logical processors caused dozens of
            // concurrent random file opens on large libraries and made the
            // WinUI window appear hung. Four workers keep storage busy without
            // saturating the thread pool or the user's disk.
            MaxDegreeOfParallelism = Math.Min(
                MaxScanConcurrency,
                Math.Max(1, Environment.ProcessorCount)),
        };

    private TCard? TryCreateCard(string filePath) =>
        TryCreateCard(new FileInfo(filePath));

    public void StartWatching(IEnumerable<string> folderPaths)
    {
        var replacement = new List<FileSystemWatcher>();
        try
        {
            foreach (var folder in folderPaths)
            {
                if (!Directory.Exists(folder))
                    continue;

                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter =
                        NotifyFilters.FileName | NotifyFilters.LastWrite,
                };
                replacement.Add(watcher);

                ConfigureWatcher(watcher);

                watcher.Created += OnFileCreated;
                watcher.Changed += OnFileChanged;
                watcher.Deleted += OnFileDeleted;
                watcher.Renamed += OnFileRenamed;
                watcher.EnableRaisingEvents = true;
            }
        }
        catch
        {
            DisposeWatchers(replacement);
            throw;
        }

        var previous = _watchers.ToList();
        _watchers.Clear();
        _watchers.AddRange(replacement);
        DisposeWatchers(previous);
    }

    public void StopWatching()
    {
        var previous = _watchers.ToList();
        _watchers.Clear();
        DisposeWatchers(previous);
    }

    private static void DisposeWatchers(
        IEnumerable<FileSystemWatcher> watchers)
    {
        foreach (var watcher in watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
        => QueueAddOrChange(e.FullPath);

    private void OnFileChanged(object sender, FileSystemEventArgs e)
        => QueueAddOrChange(e.FullPath);

    private void QueueAddOrChange(string filePath)
    {
        lock (_lock)
        {
            _pendingAddAttempts[filePath] = 0;
            _pendingChanges.Add(filePath);
            RestartDebounceTimer();
        }
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            _pendingAddAttempts.Remove(e.FullPath);
            _pendingChanges.Add(e.FullPath);
            RestartDebounceTimer();
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        lock (_lock)
        {
            _pendingAddAttempts.Remove(e.OldFullPath);
            _pendingAddAttempts[e.FullPath] = 0;
            _pendingChanges.Add(e.OldFullPath);
            _pendingChanges.Add(e.FullPath);
            RestartDebounceTimer();
        }
    }

    private void RestartDebounceTimer()
    {
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void FlushPendingChanges()
    {
        HashSet<string> changes;
        lock (_lock)
        {
            changes = [.. _pendingChanges];
            _pendingChanges.Clear();
        }

        foreach (var path in changes)
        {
            if (File.Exists(path))
            {
                var card = TryCreateCard(path);
                if (card != null)
                {
                    lock (_lock)
                        _pendingAddAttempts.Remove(path);
                    CardAdded?.Invoke(card);
                }
                else
                {
                    // The file is still there, it just does not parse as a
                    // card: a write in progress, or a file this gallery does
                    // not handle. Give up quietly once the retries run out —
                    // reporting a removal here would drop a card that is only
                    // being overwritten.
                    QueueParseRetry(path);
                }
            }
            else
            {
                lock (_lock)
                    _pendingAddAttempts.Remove(path);
                CardRemoved?.Invoke(path);
            }
        }
    }

    private void QueueParseRetry(string filePath)
    {
        if (!File.Exists(filePath))
        {
            lock (_lock)
                _pendingAddAttempts.Remove(filePath);
            return;
        }

        lock (_lock)
        {
            var attempt = _pendingAddAttempts.GetValueOrDefault(filePath) + 1;
            if (attempt >= MaxParseAttempts)
            {
                _pendingAddAttempts.Remove(filePath);
                return;
            }

            _pendingAddAttempts[filePath] = attempt;
            _pendingChanges.Add(filePath);
            RestartDebounceTimer();
        }
    }

    public void Dispose()
    {
        StopWatching();
        _debounceTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}

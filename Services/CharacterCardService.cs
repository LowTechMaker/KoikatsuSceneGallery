using System.Collections.Concurrent;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Scans + watches the configured character-card folders, mirroring
/// <see cref="SceneCardService"/>. Kept separate so character folders and scene
/// folders are tracked independently.
/// </summary>
public class CharacterCardService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges = [];
    private readonly object _lock = new();

    // Bound scan concurrency so a large library can't inject dozens of
    // thread-pool threads blocked on file I/O. ProcessorCount saturates an
    // SSD/NVMe queue for these tiny header reads without the thread churn.
    private static readonly ParallelOptions ScanOptions =
        new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    public event Action<CharacterCard>? CardAdded;
    public event Action<string>? CardRemoved;

    public CharacterCardService()
    {
        _debounceTimer = new System.Timers.Timer(300);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) => FlushPendingChanges();
    }

    public Task<List<CharacterCard>> ScanFoldersAsync(IEnumerable<string> folderPaths)
    {
        return Task.Run(() =>
        {
            var cards = new ConcurrentBag<CharacterCard>();
            foreach (var folder in folderPaths)
            {
                if (!Directory.Exists(folder)) continue;

                Parallel.ForEach(EnumerateCardFiles(folder), ScanOptions, file =>
                {
                    var card = TryCreateCard(file);
                    if (card != null)
                        cards.Add(card);
                });
            }
            return cards.ToList();
        });
    }

    public Task ScanFoldersAsync(IEnumerable<string> folderPaths, Action<List<CharacterCard>> onBatch, int batchSize = 200)
    {
        return Task.Run(() =>
        {
            var batchLock = new object();
            var batch = new List<CharacterCard>(batchSize);

            void Accumulate(CharacterCard card)
            {
                List<CharacterCard>? ready = null;
                lock (batchLock)
                {
                    batch.Add(card);
                    if (batch.Count >= batchSize)
                    {
                        ready = batch;
                        batch = new List<CharacterCard>(batchSize);
                    }
                }
                // Dispatch outside the lock so the (UI-marshaling) callback never
                // blocks other worker threads from accumulating.
                if (ready != null)
                    onBatch(ready);
            }

            foreach (var folder in folderPaths)
            {
                if (!Directory.Exists(folder)) continue;
                Parallel.ForEach(EnumerateCardFiles(folder), ScanOptions, file =>
                {
                    var card = TryCreateCard(file);
                    if (card != null)
                        Accumulate(card);
                });
            }

            if (batch.Count > 0)
                onBatch(batch);
        });
    }

    // DirectoryInfo.EnumerateFiles yields FileInfo objects whose size/timestamps
    // are already populated from the directory walk, so reading them in
    // TryCreateCard costs no extra stat per file.
    private static IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.png", SearchOption.AllDirectories);

    public void StartWatching(IEnumerable<string> folderPaths)
    {
        StopWatching();

        foreach (var folder in folderPaths)
        {
            if (!Directory.Exists(folder)) continue;

            var watcher = new FileSystemWatcher(folder, "*.png")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };

            watcher.Created += OnFileCreated;
            watcher.Deleted += OnFileDeleted;
            watcher.Renamed += OnFileRenamed;

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

    private static CharacterCard? TryCreateCard(string filePath) =>
        TryCreateCard(new FileInfo(filePath));

    private static CharacterCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            var (width, height) = PngHelper.ReadDimensions(info.FullName);
            return new CharacterCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                DateCreated = info.CreationTime,
                Width = width,
                Height = height
            };
        }
        catch (Exception)
        {
            return null;
        }
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

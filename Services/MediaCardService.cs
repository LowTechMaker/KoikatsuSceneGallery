using System.Collections.Concurrent;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class MediaCardService : IDisposable
{
    private readonly string[] _extensions;
    private readonly bool _isVideo;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges = [];
    private readonly object _lock = new();

    private static readonly ParallelOptions ScanOptions =
        new() { MaxDegreeOfParallelism = Environment.ProcessorCount };

    public event Action<MediaCard>? CardAdded;
    public event Action<string>? CardRemoved;

    public MediaCardService(string[] extensions, bool isVideo)
    {
        _extensions = extensions;
        _isVideo = isVideo;
        _debounceTimer = new System.Timers.Timer(300);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) => FlushPendingChanges();
    }

    public Task<List<MediaCard>> ScanFoldersAsync(IEnumerable<string> folderPaths)
    {
        return Task.Run(() =>
        {
            var cards = new ConcurrentBag<MediaCard>();
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

    public Task ScanFoldersAsync(IEnumerable<string> folderPaths, Action<List<MediaCard>> onBatch, int batchSize = 200)
    {
        return Task.Run(() =>
        {
            var batchLock = new object();
            var batch = new List<MediaCard>(batchSize);

            void Accumulate(MediaCard card)
            {
                List<MediaCard>? ready = null;
                lock (batchLock)
                {
                    batch.Add(card);
                    if (batch.Count >= batchSize)
                    {
                        ready = batch;
                        batch = new List<MediaCard>(batchSize);
                    }
                }
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

    private IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(f => _extensions.Contains(f.Extension, StringComparer.OrdinalIgnoreCase));

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
                EnableRaisingEvents = true
            };

            foreach (var ext in _extensions)
                watcher.Filters.Add($"*{ext}");

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

    private MediaCard? TryCreateCard(string filePath) =>
        TryCreateCard(new FileInfo(filePath));

    private MediaCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            return new MediaCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                Width = 0,
                Height = 0,
                IsVideo = _isVideo
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

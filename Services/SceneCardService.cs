using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class SceneCardService : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _pendingChanges = [];
    private readonly object _lock = new();

    public event Action<SceneCard>? CardAdded;
    public event Action<string>? CardRemoved;

    public SceneCardService()
    {
        _debounceTimer = new System.Timers.Timer(300);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (_, _) => FlushPendingChanges();
    }

    public Task<List<SceneCard>> ScanFoldersAsync(IEnumerable<string> folderPaths)
    {
        return Task.Run(() =>
        {
            var cards = new List<SceneCard>();
            foreach (var folder in folderPaths)
            {
                if (!Directory.Exists(folder)) continue;

                var files = Directory.EnumerateFiles(folder, "*.png", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var card = TryCreateCard(file);
                    if (card != null)
                        cards.Add(card);
                }
            }
            return cards;
        });
    }

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

    private static SceneCard? TryCreateCard(string filePath)
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists) return null;

            var (width, height) = PngHelper.ReadDimensions(filePath);
            return new SceneCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
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

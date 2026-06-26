using System.Collections.Concurrent;
using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

public record CachedCardEntry(
    string FilePath,
    long FileSize,
    long DateModifiedTicks,
    int Width,
    int Height,
    string? ThumbnailPath);

public sealed class SceneCardCacheService : IDisposable
{
    private const string CacheFileName = "scene_cards.json";
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, CachedCardEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _saveLock = new();
    private Timer? _saveTimer;
    private bool _dirty;
    private volatile bool _loaded;

    public SceneCardCacheService()
    {
        _cachePath = Path.Combine(AppPaths.LocalFolder, CacheFileName);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (!File.Exists(_cachePath)) return;
            using var stream = File.OpenRead(_cachePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(stream);
            if (data is null) return;
            foreach (var (key, value) in data)
                _cache[key] = value;
        }
        catch (Exception)
        {
        }
    }

    public Dictionary<string, CachedCardEntry> LoadAll()
    {
        EnsureLoaded();
        return new Dictionary<string, CachedCardEntry>(_cache, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateAll(IReadOnlyDictionary<string, CachedCardEntry> entries)
    {
        _cache.Clear();
        foreach (var (key, value) in entries)
            _cache[key] = value;
        ScheduleSave();
    }

    public void Add(CachedCardEntry entry)
    {
        _cache[entry.FilePath] = entry;
        ScheduleSave();
    }

    public void Remove(string filePath)
    {
        if (_cache.TryRemove(filePath, out _))
            ScheduleSave();
    }

    public void SetThumbnailPath(string filePath, string thumbnailPath)
    {
        if (_cache.TryGetValue(filePath, out var existing))
        {
            _cache[filePath] = existing with { ThumbnailPath = thumbnailPath };
            ScheduleSave();
        }
    }

    private void ScheduleSave()
    {
        lock (_saveLock)
        {
            _dirty = true;
            _saveTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(2000, Timeout.Infinite);
        }
    }

    private void Flush()
    {
        Dictionary<string, CachedCardEntry> snapshot;
        lock (_saveLock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = new Dictionary<string, CachedCardEntry>(_cache, StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var tempPath = _cachePath + ".tmp";
            using (var stream = File.Create(tempPath))
                JsonSerializer.Serialize(stream, snapshot);
            File.Move(tempPath, _cachePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        _saveTimer?.Dispose();
        Flush();
    }
}

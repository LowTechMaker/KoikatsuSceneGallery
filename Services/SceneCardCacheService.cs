using System.Collections.Concurrent;
using System.Text.Json;
using SceneGallery.PluginCommon;
using KoikatsuSceneGallery.Helpers;

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
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, CachedCardEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    private readonly DebouncedDiskPersistence _persistence;
    private readonly OperationDrain _operations = new();
    private readonly CacheLoadGate _loadGate = new();

    public SceneCardCacheService(IAppLogger logger)
        : this(logger, Path.Combine(AppPaths.LocalFolder, CacheFileName),
            (path, serialize, report) => new DebouncedDiskPersistence(path, serialize, report))
    {
    }

    internal SceneCardCacheService(IAppLogger logger, string cachePath,
        Func<string, Action<Stream>, Action<Exception>, DebouncedDiskPersistence> createPersistence)
    {
        _logger = logger;
        _cachePath = cachePath;
        _persistence = createPersistence(_cachePath,
            stream => JsonSerializer.Serialize(stream,
                new Dictionary<string, CachedCardEntry>(_cache, StringComparer.OrdinalIgnoreCase)),
            ex => _logger.LogError("SceneCardCache.Flush", ex, _cachePath));
    }

    private void EnsureLoaded()
    {
        _loadGate.EnsureLoaded(() =>
        {
            try
            {
                if (!File.Exists(_cachePath)) return false;
                using var stream = File.OpenRead(_cachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(stream);
                if (data is null) return false;
                foreach (var (key, value) in data)
                    _cache[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError("SceneCardCache.Load", ex, _cachePath);
            }
            return true;
        });
    }

    public Dictionary<string, CachedCardEntry> LoadAll()
    {
        _operations.TryRun(EnsureLoaded);
        return new Dictionary<string, CachedCardEntry>(_cache, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateAll(IReadOnlyDictionary<string, CachedCardEntry> entries)
    {
        _operations.TryRun(() =>
        {
            _cache.Clear();
            foreach (var (key, value) in entries)
                _cache[key] = value;
            ScheduleSave();
        });
    }

    public void Add(CachedCardEntry entry)
    {
        _operations.TryRun(() =>
        {
            _cache[entry.FilePath] = entry;
            ScheduleSave();
        });
    }

    public void Remove(string filePath)
    {
        _operations.TryRun(() =>
        {
            if (_cache.TryRemove(filePath, out _))
                ScheduleSave();
        });
    }

    public void SetThumbnailPath(string filePath, string thumbnailPath)
    {
        _operations.TryRun(() =>
        {
            if (_cache.TryGetValue(filePath, out var existing))
            {
                _cache[filePath] = existing with { ThumbnailPath = thumbnailPath };
                ScheduleSave();
            }
        });
    }

    private void ScheduleSave() => _persistence.MarkDirty();

    internal Task StopAsync() => _operations.StopAsync();

    // Normal shutdown awaits StopAsync first. Direct disposal still defers persistence
    // until an already accepted synchronous mutation has left the service.
    public void Dispose()
    {
        var drained = StopAsync();
        if (drained.IsCompletedSuccessfully) _persistence.Dispose();
        else DisposeAfterDrainAsync(drained).Observe(_logger, "SceneCardCache.Dispose");
    }

    private async Task DisposeAfterDrainAsync(Task drained)
    {
        await drained.ConfigureAwait(false);
        _persistence.Dispose();
    }
}

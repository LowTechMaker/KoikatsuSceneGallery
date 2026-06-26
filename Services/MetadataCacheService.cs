using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public abstract class MetadataCacheService<TCard, TMetadata>
    where TCard : CardBase
    where TMetadata : class
{
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, TMetadata> _cache = new();
    private readonly JsonSerializerOptions? _jsonOptions;

    private readonly Lock _saveLock = new();
    private Timer? _saveTimer;
    private bool _dirty;

    private readonly Lock _loadLock = new();
    private volatile bool _loaded;

    protected MetadataCacheService(string cacheFileName, JsonSerializerOptions? jsonOptions = null)
    {
        _cachePath = Path.Combine(AppPaths.LocalFolder, cacheFileName);
        _jsonOptions = jsonOptions;
    }

    protected abstract TMetadata Parse(TCard card);

    private void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_loadLock)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(_cachePath)) return;
                using var stream = File.OpenRead(_cachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, TMetadata>>(stream, _jsonOptions);
                if (data is null) return;
                foreach (var (key, value) in data)
                    _cache[key] = value;
            }
            catch (Exception)
            {
            }
        }
    }

    public bool TryGetCached(TCard card, out TMetadata metadata)
    {
        EnsureLoaded();
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        return _cache.TryGetValue(key, out metadata!);
    }

    public TMetadata ParseAndCache(TCard card)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var metadata = Parse(card);
        _cache[key] = metadata;
        ScheduleSave();
        return metadata;
    }

    public void Invalidate(TCard card)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        if (_cache.TryRemove(key, out _))
            ScheduleSave();
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
        Dictionary<string, TMetadata> snapshot;
        lock (_saveLock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = new Dictionary<string, TMetadata>(_cache);
        }
        try
        {
            var tempPath = _cachePath + ".tmp";
            using (var stream = File.Create(tempPath))
                JsonSerializer.Serialize(stream, snapshot, _jsonOptions);
            File.Move(tempPath, _cachePath, overwrite: true);
        }
        catch (Exception)
        {
        }
    }

    private static string ComputeCacheKey(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}

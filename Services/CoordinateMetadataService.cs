using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public sealed class CoordinateMetadataService
{
    private const string CacheFileName = "coord_metadata.json";
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, CoordinateMetadata> _cache = new();

    private readonly Lock _saveLock = new();
    private Timer? _saveTimer;
    private bool _dirty;

    private volatile bool _loaded;

    public CoordinateMetadataService()
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
            var data = JsonSerializer.Deserialize<Dictionary<string, CoordinateMetadata>>(stream);
            if (data is null) return;
            foreach (var (key, value) in data)
                _cache[key] = value;
        }
        catch (Exception)
        {
        }
    }

    public bool TryGetCached(CoordinateCard card, out CoordinateMetadata metadata)
    {
        EnsureLoaded();
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        return _cache.TryGetValue(key, out metadata!);
    }

    public CoordinateMetadata ParseAndCache(CoordinateCard card)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var metadata = CoordinateCardParser.TryParse(card.FilePath) ?? CoordinateMetadata.Unknown;
        _cache[key] = metadata;
        ScheduleSave();
        return metadata;
    }

    public void Invalidate(CoordinateCard card)
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
        Dictionary<string, CoordinateMetadata> snapshot;
        lock (_saveLock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = new Dictionary<string, CoordinateMetadata>(_cache);
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

    private static string ComputeCacheKey(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}

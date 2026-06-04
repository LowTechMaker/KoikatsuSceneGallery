using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Parses + classifies scene plugin metadata, backed by a persistent on-disk
/// cache keyed by file path + last-modified time (same scheme as the thumbnail
/// cache). Parsing is left to the caller's thread; the cache is written in
/// debounced batches so repeated scans cost only a single small JSON write.
/// </summary>
public sealed class SceneMetadataService
{
    private const string CacheFileName = "scene_metadata.json";
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, SceneMetadata> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Lock _saveLock = new();
    private Timer? _saveTimer;
    private bool _dirty;

    public SceneMetadataService()
    {
        _cachePath = Path.Combine(AppPaths.LocalFolder, CacheFileName);
        Load();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            using var stream = File.OpenRead(_cachePath);
            var data = JsonSerializer.Deserialize<Dictionary<string, SceneMetadata>>(stream, JsonOptions);
            if (data is null) return;
            foreach (var (key, value) in data)
                _cache[key] = value;
        }
        catch (Exception)
        {
            // Corrupt or unreadable cache — start fresh.
        }
    }

    /// <summary>Returns cached metadata for the card if present.</summary>
    public bool TryGetCached(SceneCard card, out SceneMetadata metadata)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        return _cache.TryGetValue(key, out metadata!);
    }

    /// <summary>
    /// Returns cached metadata, or parses + classifies the scene (on the calling
    /// thread) and caches the result. Intended to be called from a background
    /// thread with bounded concurrency.
    /// </summary>
    public SceneMetadata ParseAndCache(SceneCard card)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var parsed = SceneMetadataParser.TryParse(card.FilePath);
        var metadata = SceneClassifier.Classify(parsed);
        _cache[key] = metadata;
        ScheduleSave();
        return metadata;
    }

    /// <summary>Drops the cached entry for a card so it will be re-parsed.</summary>
    public void Invalidate(SceneCard card)
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
        Dictionary<string, SceneMetadata> snapshot;
        lock (_saveLock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = new Dictionary<string, SceneMetadata>(_cache);
        }
        try
        {
            var tempPath = _cachePath + ".tmp";
            using (var stream = File.Create(tempPath))
                JsonSerializer.Serialize(stream, snapshot, JsonOptions);
            File.Move(tempPath, _cachePath, overwrite: true);
        }
        catch (Exception)
        {
            // Best-effort persistence; an unsaved cache just gets re-parsed.
        }
    }

    private static string ComputeCacheKey(string filePath, DateTime dateModified)
    {
        var input = $"{filePath}|{dateModified.Ticks}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash)[..16];
    }
}

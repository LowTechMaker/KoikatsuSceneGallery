using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Parses + caches character-card metadata, backed by a persistent on-disk cache
/// keyed by file path + last-modified time (same scheme as the thumbnail and
/// scene-metadata caches). Mirrors <see cref="SceneMetadataService"/>.
/// </summary>
public sealed class CharacterMetadataService
{
    private const string CacheFileName = "chara_metadata.json";
    private readonly string _cachePath;
    private readonly ConcurrentDictionary<string, CharacterMetadata> _cache = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Lock _saveLock = new();
    private Timer? _saveTimer;
    private bool _dirty;

    public CharacterMetadataService()
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
            var data = JsonSerializer.Deserialize<Dictionary<string, CharacterMetadata>>(stream, JsonOptions);
            if (data is null) return;
            foreach (var (key, value) in data)
                _cache[key] = value;
        }
        catch (Exception)
        {
            // Corrupt or unreadable cache — start fresh.
        }
    }

    public bool TryGetCached(CharacterCard card, out CharacterMetadata metadata)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        return _cache.TryGetValue(key, out metadata!);
    }

    /// <summary>
    /// Returns cached metadata, or parses the card (on the calling thread) and
    /// caches the result. Unparseable cards cache as <see cref="CharacterMetadata.Unknown"/>
    /// so they aren't retried on every scan. Intended for background threads.
    /// </summary>
    public CharacterMetadata ParseAndCache(CharacterCard card)
    {
        var key = ComputeCacheKey(card.FilePath, card.DateModified);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var metadata = CharacterCardParser.TryParse(card.FilePath) ?? CharacterMetadata.Unknown;
        _cache[key] = metadata;
        ScheduleSave();
        return metadata;
    }

    public void Invalidate(CharacterCard card)
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
        Dictionary<string, CharacterMetadata> snapshot;
        lock (_saveLock)
        {
            if (!_dirty) return;
            _dirty = false;
            snapshot = new Dictionary<string, CharacterMetadata>(_cache);
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

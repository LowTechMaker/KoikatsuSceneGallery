using System.Collections.Concurrent;
using System.Text.Json;

namespace SceneGallery.Plugin.PixivAuthors;

/// <summary>
/// Disk-backed author cache (authors.json in the plugin's storage directory).
/// Successful entries live until a forced refresh; failed lookups are cached
/// with a TTL so dead ids aren't re-queried every launch. Saves are debounced
/// and flushed via temp-file + move. A corrupt cache file is discarded.
/// </summary>
internal sealed class AuthorDiskCache : IDisposable
{
    public sealed record CachedAuthor(
        string? Name,
        string? AvatarFile,
        DateTimeOffset FetchedAt,
        bool Failed);

    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FailedEntryTtl = TimeSpan.FromDays(7);

    private readonly ConcurrentDictionary<string, CachedAuthor> _entries = new();
    private readonly string _cachePath;
    private readonly Action<string> _log;
    private readonly Timer _saveTimer;
    private readonly Lock _saveLock = new();
    private volatile bool _dirty;

    public AuthorDiskCache(string storageDirectory, Action<string> log)
    {
        _cachePath = Path.Combine(storageDirectory, "authors.json");
        _log = log;
        _saveTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Load();
    }

    public bool TryGet(string id, out CachedAuthor entry)
    {
        if (!_entries.TryGetValue(id, out entry!)) return false;
        if (entry.Failed && DateTimeOffset.UtcNow - entry.FetchedAt > FailedEntryTtl)
        {
            _entries.TryRemove(id, out _);
            return false;
        }
        return true;
    }

    public void Set(string id, CachedAuthor entry)
    {
        _entries[id] = entry;
        _dirty = true;
        _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            using var stream = File.OpenRead(_cachePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, CachedAuthor>>(stream);
            if (loaded is null) return;
            foreach (var (id, entry) in loaded)
                _entries[id] = entry;
        }
        catch (Exception ex)
        {
            _log($"author cache unreadable, starting fresh: {ex.Message}");
        }
    }

    private void Flush()
    {
        if (!_dirty) return;
        _dirty = false;
        try
        {
            lock (_saveLock)
            {
                var tempPath = _cachePath + ".tmp";
                using (var stream = File.Create(tempPath))
                    JsonSerializer.Serialize(stream, _entries);
                File.Move(tempPath, _cachePath, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log($"author cache save failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}

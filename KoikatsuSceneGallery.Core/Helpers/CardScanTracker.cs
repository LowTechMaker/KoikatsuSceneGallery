using System.Collections.Concurrent;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// What a scene scan saw, for a library already on screen from its cache. A
/// listed file the cache still describes (<see cref="CardCacheReuse.Describes"/>)
/// is skipped in the scan worker: it is neither opened nor sent to the UI
/// thread, where it would only be found equal to the card already there. The
/// scan still has to know it saw the file, or the card would be removed as
/// gone; this records that, and the files that were read, from any thread.
/// </summary>
internal sealed class CardScanTracker(IReadOnlyDictionary<string, CachedCardEntry> cache)
{
    private readonly ConcurrentDictionary<string, DateTime> _seen = new(StringComparer.OrdinalIgnoreCase);
    private int _skipped;

    /// <summary>Cached files skipped as unchanged.</summary>
    public int Skipped => Volatile.Read(ref _skipped);

    /// <summary>
    /// True when the cache describes the listed file: it is then seen, and the
    /// caller drops it. False: the caller opens it, and calls
    /// <see cref="MarkRead"/> if it became a card.
    /// </summary>
    public bool SkipUnchanged(string path, long length, DateTime lastWriteTime, DateTime creationTime)
    {
        if (!CardCacheReuse.Describes(cache.GetValueOrDefault(path), length, lastWriteTime)) return false;
        _seen[path] = creationTime;
        Interlocked.Increment(ref _skipped);
        return true;
    }

    /// <summary>A file the scan opened and turned into a card.</summary>
    public void MarkRead(string path, DateTime creationTime) => _seen[path] = creationTime;

    /// <summary>The paths of <paramref name="known"/> the scan neither skipped nor read: gone from disk, or unreadable now.</summary>
    public IReadOnlyList<string> Missing(IEnumerable<string> known) => [.. known.Where(path => !_seen.ContainsKey(path))];

    /// <summary>The creation time the listing gave for a seen file; null for one not seen.</summary>
    public DateTime? CreatedOf(string path) => _seen.TryGetValue(path, out var created) ? created : null;
}

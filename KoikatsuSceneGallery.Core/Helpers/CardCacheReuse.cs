using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// When the scene card cache can stand in for opening a file, and when the
/// cache file needs writing at all. The start of the app used to open every
/// card file to read its PNG header although the cache already had the
/// dimensions, and to rewrite the whole cache although nothing had changed.
/// </summary>
internal static class CardCacheReuse
{
    /// <summary>
    /// Whether <paramref name="entry"/> still describes a listed file: same size
    /// and same last write time, as the directory listing gives them (no open).
    /// Anything else (no entry, written since, a different size with the same
    /// write time, an entry without dimensions) means the file is opened.
    /// </summary>
    public static bool Describes(CachedCardEntry? entry, long length, DateTime lastWriteTime)
        => entry is not null
           && entry.FileSize == length
           && entry.DateModifiedTicks == lastWriteTime.Ticks
           && entry.Width > 0
           && entry.Height > 0;

    /// <summary>Whether two caches hold the same entries (keys compared by the dictionaries' own comparers).</summary>
    public static bool SameEntries(IReadOnlyDictionary<string, CachedCardEntry> a, IReadOnlyDictionary<string, CachedCardEntry> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var other) && other == p.Value);
}

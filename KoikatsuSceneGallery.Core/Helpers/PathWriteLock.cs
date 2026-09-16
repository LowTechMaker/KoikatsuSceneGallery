using System.Collections.Concurrent;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// One process-wide write lock per document path.
/// </summary>
/// <remarks>
/// The three library document stores each kept their own identical dictionary
/// of per-path semaphores. Only the lock registry is shared: acquiring,
/// releasing and everything the caller does while holding the lock stay at the
/// call site, because each store has its own read-merge-write sequence.
///
/// The semaphores are never removed. A library has a bounded number of document
/// paths, and dropping an entry while a writer held it would let a second
/// writer create a fresh semaphore and enter the same file concurrently.
///
/// Sharing one registry means two different stores writing the same path now
/// exclude each other, where before they only excluded writers of their own
/// kind. The stores use distinct file names inside the metadata directory, so
/// no path is reachable from more than one of them today.
/// </remarks>
internal static class PathWriteLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(PathComparison.Comparer);

    /// <summary>
    /// Returns the lock guarding <paramref name="path"/>. The caller waits on it
    /// and must release it in a finally.
    /// </summary>
    internal static SemaphoreSlim For(string path)
        => Locks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
}

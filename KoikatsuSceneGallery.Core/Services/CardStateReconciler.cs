using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Finds cards whose stored state points at a path that no longer exists,
/// because the file was moved or renamed while the app was not looking, and
/// re-attaches the state to the card with the same payload hash.
/// </summary>
/// <remarks>
/// The shelves are built from stored paths, and nothing else hashes a card
/// unless it already has one in hand, so without this a card moved in
/// Explorer silently loses its state. Hashing reads the card data, which is
/// the whole cost, so the search hashes as little as it can:
/// <list type="bullet">
/// <item>nothing at all when no stored path is missing;</item>
/// <item>a missing path counts only in a root that has at least one live card,
/// so a library not loaded yet or an unmounted drive starts no search;</item>
/// <item>only live cards that have no stored state (a card with one is not the orphan);</item>
/// <item>likely candidates first — the orphan's file name, then its folder —
/// and it stops as soon as every orphan is matched;</item>
/// <item>every hash computed is remembered by path for the life of this
/// instance, so later runs look it up instead of reading the file again.</item>
/// </list>
/// An orphan with no match keeps its state untouched: the card may be on a
/// drive that is not mounted right now.
/// </remarks>
internal sealed class CardStateReconciler(CardStateStore store, Func<string, CancellationToken, string?> computeHash)
{
    private readonly object _gate = new();
    // Null when the file could not be hashed (not a card, unreadable): it is
    // not tried again this session either.
    private readonly Dictionary<string, string?> _hashByPath = new(PathComparison.Comparer);

    /// <summary>
    /// Re-attaches orphaned states to the cards in <paramref name="livePaths"/>.
    /// Returns how many were re-attached; each one also raises
    /// <see cref="CardStateStore.StateChanged"/> with its new path.
    /// </summary>
    public async Task<int> ReconcileAsync(IReadOnlyCollection<string> livePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(livePaths);
        cancellationToken.ThrowIfCancellationRequested();

        var live = new HashSet<string>(livePaths.Select(Path.GetFullPath), PathComparison.Comparer);
        // A root with no live card at all is a library that has not loaded
        // yet or a drive that is not mounted: every state there would look
        // orphaned, and searching for them would hash the other libraries for
        // nothing. The price: a root emptied completely is not reconciled.
        var liveRoots = live.Select(store.RootOf).OfType<string>().ToHashSet(PathComparison.Comparer);
        var orphans = new Dictionary<string, string>(StringComparer.Ordinal); // hash -> stored full path
        foreach (var (fullPath, state) in store.Snapshot())
        {
            if (!live.Contains(fullPath) && store.RootOf(fullPath) is { } root && liveRoots.Contains(root))
                orphans[state.Hash] = fullPath;
        }
        if (orphans.Count == 0) return 0;

        var names = orphans.Values.Select(Path.GetFileName).ToHashSet(PathComparison.Comparer);
        var folders = orphans.Values.Select(Path.GetDirectoryName).OfType<string>().ToHashSet(PathComparison.Comparer);
        // A card that already has a state is somebody's, not the orphan's.
        var candidates = live
            .Where(path => store.TryGetByPath(path) is null)
            .OrderBy(path => names.Contains(Path.GetFileName(path)) ? 0
                : folders.Contains(Path.GetDirectoryName(path) ?? "") ? 1
                : 2)
            .ToList();

        var reattached = 0;
        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? hash;
            bool known;
            lock (_gate) known = _hashByPath.TryGetValue(path, out hash);
            if (!known)
            {
                hash = computeHash(path, cancellationToken);
                lock (_gate) _hashByPath[path] = hash;
            }

            if (hash is null || !orphans.Remove(hash)) continue;
            // ResolveAsync rewrites the stored path (moving the entry to
            // another root's file when needed) and raises StateChanged. It
            // leaves the path alone when the card is outside every loaded root
            // or a root is read-only, so count only what now resolves here.
            await store.ResolveAsync(path, hash, cancellationToken).ConfigureAwait(false);
            if (store.TryGetByPath(path) is not null) reattached++;
            if (orphans.Count == 0) break;
        }
        return reattached;
    }
}

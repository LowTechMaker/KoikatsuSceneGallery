namespace KoikatsuSceneGallery.Helpers;

/// <summary>Cheap in-memory features for anti-clustering. Never derived from image pixels.</summary>
internal readonly record struct DiscoveryClusterKey(long FileSize, string FolderKey);

/// <summary>
/// Spreads visually similar cards apart inside a freshly drawn batch, so a uniform shuffle does not
/// happen to place look-alikes next to each other in the grid. Reorders only: the batch that comes
/// out is the same multiset that went in, so <see cref="DiscoveryShuffle{T}"/>'s guarantee of
/// covering every candidate exactly once per round is untouched.
/// </summary>
internal static class DiscoveryAntiClustering
{
    /// <summary>Bounds the work per position; a batch of 40 costs at most ~640 comparisons.</summary>
    private const int Lookahead = 16;

    // A near-identical export has a near-identical file size, so that is the stronger signal.
    // Sharing a folder is weak on its own: in a single-folder library every pair matches, which is
    // exactly why the scores are weighted instead of boolean. There the folder term cancels out
    // across all candidates and file size still decides, rather than the whole pass going no-op.
    private const int SizeWeight = 3;
    private const int FolderWeight = 1;

    private const long SizeFloor = 4096;
    private const long SizeRatio = 100;

    /// <param name="batch">Items drawn but not yet shown; reordered in place.</param>
    /// <param name="key">Feature extraction. Must not touch the disk.</param>
    /// <param name="columns">Live column count of the grid.</param>
    /// <param name="placedTail">The last <paramref name="columns"/> items already in the grid, in display order.</param>
    public static void Spread<T>(
        IList<T> batch,
        Func<T, DiscoveryClusterKey> key,
        int columns,
        IReadOnlyList<T> placedTail)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(placedTail);
        if (columns <= 0 || batch.Count < 2) return;

        // Only the last row of what is already shown can neighbour the new batch.
        int tailCount = Math.Min(placedTail.Count, columns);
        int tailOffset = placedTail.Count - tailCount;
        var tailKeys = new DiscoveryClusterKey[tailCount];
        for (int i = 0; i < tailCount; i++) tailKeys[i] = key(placedTail[tailOffset + i]);

        var batchKeys = new DiscoveryClusterKey[batch.Count];
        for (int i = 0; i < batch.Count; i++) batchKeys[i] = key(batch[i]);

        // The virtual grid is tail followed by batch; batch[i] sits at absolute slot tailCount + i.
        DiscoveryClusterKey At(int slot) => slot < tailCount ? tailKeys[slot] : batchKeys[slot - tailCount];

        for (int i = 0; i < batch.Count; i++)
        {
            int slot = tailCount + i;
            int left = slot % columns == 0 ? -1 : slot - 1;
            int above = slot - columns;

            int current = Score(batchKeys[i], left, above, At);
            if (current == 0) continue;

            int bestIndex = -1;
            int bestScore = current;
            int limit = Math.Min(batch.Count, i + 1 + Lookahead);
            for (int j = i + 1; j < limit; j++)
            {
                int candidate = Score(batchKeys[j], left, above, At);
                if (candidate >= bestScore) continue;
                bestScore = candidate;
                bestIndex = j;
                if (candidate == 0) break;
            }

            // No improvement in reach: keep the original item rather than forcing a worse swap.
            if (bestIndex < 0) continue;

            (batch[i], batch[bestIndex]) = (batch[bestIndex], batch[i]);
            (batchKeys[i], batchKeys[bestIndex]) = (batchKeys[bestIndex], batchKeys[i]);
            // The displaced item is re-examined when the scan reaches bestIndex.
        }
    }

    private static int Score(
        DiscoveryClusterKey candidate,
        int left,
        int above,
        Func<int, DiscoveryClusterKey> at)
    {
        int score = 0;
        // A negative slot is off the grid: no left neighbour in column 0, nothing above the first row.
        if (left >= 0) score += Similarity(candidate, at(left));
        if (above >= 0) score += Similarity(candidate, at(above));
        return score;
    }

    private static int Similarity(DiscoveryClusterKey a, DiscoveryClusterKey b)
    {
        int score = 0;
        if (a.FileSize > 0 && b.FileSize > 0)
        {
            long tolerance = Math.Max(SizeFloor, Math.Min(a.FileSize, b.FileSize) / SizeRatio);
            if (Math.Abs(a.FileSize - b.FileSize) <= tolerance) score += SizeWeight;
        }
        if (a.FolderKey.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(a.FolderKey, b.FolderKey))
            score += FolderWeight;
        return score;
    }
}

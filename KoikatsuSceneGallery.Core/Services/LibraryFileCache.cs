namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Holds a snapshot of library PNG files grouped by file name. The snapshot is
/// immutable after publication, so destination resolution can read it safely
/// while a completed import publishes a replacement snapshot.
/// </summary>
internal sealed class LibraryFileCache
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private Snapshot? _snapshot;

    public async Task<LibraryFileCacheResult> GetOrBuildAsync(
        IEnumerable<string> roots,
        Func<CancellationToken, Dictionary<string, List<string>>> buildIndex,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(buildIndex);

        var normalizedRoots = NormalizeRoots(roots);
        var key = string.Join("\n", normalizedRoots);

        lock (_sync)
        {
            if (_snapshot is { } snapshot && snapshot.Key == key)
                return new LibraryFileCacheResult(snapshot.FilesByName, 0, WasRebuilt: false);
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_snapshot is { } snapshot && snapshot.Key == key)
                    return new LibraryFileCacheResult(snapshot.FilesByName, 0, WasRebuilt: false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var freshIndex = buildIndex(cancellationToken);
            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();

            var publishedIndex = FreezeIndex(freshIndex);
            lock (_sync)
            {
                _snapshot = new Snapshot(key, normalizedRoots, publishedIndex);
            }

            return new LibraryFileCacheResult(
                publishedIndex,
                stopwatch.ElapsedMilliseconds,
                WasRebuilt: true);
        }
        finally
        {
            _buildLock.Release();
        }
    }

    /// <summary>
    /// Adds successfully committed destination files to a valid cache without
    /// another recursive scan. Files outside the indexed roots are ignored.
    /// </summary>
    public void RegisterCommittedFiles(IEnumerable<string> destinationPaths)
    {
        ArgumentNullException.ThrowIfNull(destinationPaths);

        var paths = destinationPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (paths.Length == 0) return;

        lock (_sync)
        {
            if (_snapshot is not { } snapshot) return;

            var relevantPaths = paths
                .Where(path => snapshot.Roots.Any(root => IsUnderRoot(path, root)))
                .ToArray();
            if (relevantPaths.Length == 0) return;

            var nextIndex = snapshot.FilesByName.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
            foreach (var path in relevantPaths)
            {
                var fileName = Path.GetFileName(path);
                if (!nextIndex.TryGetValue(fileName, out var entries))
                {
                    entries = [];
                    nextIndex[fileName] = entries;
                }

                if (!entries.Contains(path, StringComparer.OrdinalIgnoreCase))
                    entries.Add(path);
            }

            _snapshot = snapshot with { FilesByName = FreezeIndex(nextIndex) };
        }
    }

    /// <summary>
    /// Makes the next resolution rebuild the index. This is intended for an
    /// explicit library refresh or another known external library mutation.
    /// </summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _snapshot = null;
        }
    }

    private static string[] NormalizeRoots(IEnumerable<string> roots)
        => roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsUnderRoot(string path, string root)
    {
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, IReadOnlyList<string>> FreezeIndex(
        IReadOnlyDictionary<string, List<string>> index)
    {
        var copy = new Dictionary<string, IReadOnlyList<string>>(
            index.Count,
            StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, paths) in index)
            copy[fileName] = paths.ToArray();
        return copy;
    }

    private sealed record Snapshot(
        string Key,
        IReadOnlyList<string> Roots,
        Dictionary<string, IReadOnlyList<string>> FilesByName);
}

internal sealed record LibraryFileCacheResult(
    IReadOnlyDictionary<string, IReadOnlyList<string>> FilesByName,
    long ScanElapsedMilliseconds,
    bool WasRebuilt);

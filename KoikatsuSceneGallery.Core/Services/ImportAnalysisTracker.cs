namespace KoikatsuSceneGallery.Services;

internal readonly record struct ImportAnalysisProgress(bool IsAnalyzing, int TotalCount, int CompletedCount);

/// <summary>State operations belong to the UI thread. Background work may only use batch tokens.</summary>
internal sealed class ImportAnalysisTracker
{
    private readonly HashSet<Batch> _active = [];
    private long _generation;

    public Batch Begin(IReadOnlyList<string> paths)
    {
        var batch = new Batch(this, _generation, paths);
        _active.Add(batch);
        return batch;
    }

    public bool ContainsPath(string path) =>
        _active.Any(batch => batch.Generation == _generation && batch.Paths.Contains(path));

    public void CancelAll()
    {
        foreach (var batch in _active.ToArray()) batch.Cancel();
    }

    // Reset progress without abandoning cancellation ownership for unfinished work.
    public void ResetProgress() => _generation++;

    public ImportAnalysisProgress GetProgress(IEnumerable<string> completedPaths)
    {
        var batches = _active.Where(batch => batch.Generation == _generation).ToArray();
        var paths = batches.SelectMany(batch => batch.Paths).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completed = completedPaths.Where(paths.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var rejected = batches.Sum(batch => (long)batch.RejectedCount);
        return new(_active.Count > 0, paths.Count, (int)Math.Clamp(completed + rejected, 0, paths.Count));
    }

    internal sealed class Batch : IDisposable
    {
        private readonly ImportAnalysisTracker _owner;
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;
        internal long Generation { get; }
        internal HashSet<string> Paths { get; }
        internal int RejectedCount { get; private set; }
        public CancellationToken Token { get; }

        internal Batch(ImportAnalysisTracker owner, long generation, IReadOnlyList<string> paths)
        {
            _owner = owner;
            Generation = generation;
            Paths = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            Token = _cts.Token;
        }

        public void AddRejectedCount(int count)
        {
            if (!_disposed && count > 0)
                RejectedCount = (int)Math.Min(Paths.Count, (long)RejectedCount + count);
        }

        public void Cancel()
        {
            if (!_disposed) _cts.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owner._active.Remove(this);
            _cts.Dispose();
        }
    }
}

namespace KoikatsuSceneGallery.Helpers;

/// <summary>A finite, non-repeating round. Drawing never silently starts another round.</summary>
internal sealed class DiscoveryShuffle<T>(Random random, IEqualityComparer<T>? comparer = null) where T : notnull
{
    private readonly IEqualityComparer<T> _comparer = comparer ?? EqualityComparer<T>.Default;
    private HashSet<T> _pool = new(comparer);
    private readonly HashSet<T> _drawn = new(comparer);
    private readonly List<T> _pending = [];
    private T? _last;
    private bool _hasLast;

    public int Total => _pool.Count;
    public int Remaining => _pending.Count;
    public int Drawn { get; private set; }
    public int Round { get; private set; }

    public void Reset(IEnumerable<T> candidates)
    {
        Round = 0;
        _hasLast = false;
        StartRound(candidates);
    }

    public void StartRound(IEnumerable<T> candidates)
    {
        _pool = new(candidates, _comparer);
        _drawn.Clear();
        Drawn = 0;
        _pending.Clear();
        _pending.AddRange(_pool);
        Shuffle(_pending);
        // Take consumes from the end. Avoid an immediate repeat across the boundary.
        if (_hasLast && _pending.Count > 1 && _comparer.Equals(_pending[^1], _last!))
            (_pending[0], _pending[^1]) = (_pending[^1], _pending[0]);
        Round++;
    }

    /// <summary>Reconcile live files without moving already displayed cards or redrawing a returned file.</summary>
    public void Synchronize(IEnumerable<T> candidates)
    {
        var next = new HashSet<T>(candidates, _comparer);
        _pending.RemoveAll(item => !next.Contains(item));
        var additions = next.Where(item => !_pool.Contains(item) && !_drawn.Contains(item)).ToList();
        Shuffle(additions);
        _pending.AddRange(additions);
        _pool = next;
        Drawn = _pool.Count(item => _drawn.Contains(item));
    }

    public IReadOnlyList<T> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var result = new List<T>(Math.Min(count, Remaining));
        while (result.Count < count && _pending.Count > 0)
        {
            var item = _pending[^1];
            _pending.RemoveAt(_pending.Count - 1);
            _drawn.Add(item);
            Drawn++;
            result.Add(item);
            _last = item;
            _hasLast = true;
        }
        return result;
    }

    private void Shuffle(List<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

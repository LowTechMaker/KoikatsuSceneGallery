namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Keeps the most recently used values within a fixed entry count.
/// Callers provide any required synchronization; this type deliberately has
/// no locking so UI-affine values can stay on their owning thread.
/// </summary>
public sealed class BoundedLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _entries;
    private readonly LinkedList<Entry> _recency = [];

    public BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public int Count => _entries.Count;

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!_entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        _recency.Remove(node);
        _recency.AddFirst(node);
        value = node.Value.Value;
        return true;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        if (TryGetValue(key, out var existing))
            return existing;

        var value = valueFactory(key);
        if (_entries.Count == _capacity)
        {
            var leastRecent = _recency.Last!;
            _entries.Remove(leastRecent.Value.Key);
            _recency.RemoveLast();
        }

        var entry = new Entry(key, value);
        _entries.Add(key, _recency.AddFirst(entry));
        return value;
    }

    private sealed record Entry(TKey Key, TValue Value);
}

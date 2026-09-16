using System.Collections;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// UI-thread-owned shuffle state. Callers supply their current source and filter;
/// display sets are snapshots while the comparer follows the current queue.
/// </summary>
internal sealed class GalleryShuffleQueue
{
    private readonly int _poolSize;
    private readonly Func<int, int> _next;
    private readonly List<object> _shuffleQueue = [];
    private readonly Dictionary<object, int> _shuffleOrderMap = [];
    private readonly HashSet<object> _shuffleUsedCards = [];

    public GalleryShuffleQueue(int poolSize, Func<int, int>? next = null)
    {
        _poolSize = poolSize;
        _next = next ?? Random.Shared.Next;
        Comparer = new ShuffleQueueComparer(_shuffleOrderMap);
    }

    public IComparer Comparer { get; }

    public void Build(IEnumerable source, Func<object, bool> passesFilter)
    {
        var candidates = new List<object>();
        foreach (object? card in source)
            if (card is not null && passesFilter(card))
                candidates.Add(card);

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = _next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int poolSize = Math.Min(_poolSize, candidates.Count);
        _shuffleQueue.Clear();
        _shuffleUsedCards.Clear();
        _shuffleOrderMap.Clear();

        for (int i = 0; i < poolSize; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleOrderMap[candidates[i]] = i;
            _shuffleUsedCards.Add(candidates[i]);
        }
    }

    public void Advance(IEnumerable source, Func<object, bool> passesFilter, int displayLimit)
    {
        int displayCount = Math.Min(displayLimit, _shuffleQueue.Count);
        if (displayCount <= 0 || _shuffleQueue.Count == 0) return;

        var tail = _shuffleQueue.Skip(displayCount).ToList();

        var candidates = new List<object>();
        foreach (object? card in source)
            if (card is not null && passesFilter(card) && !_shuffleUsedCards.Contains(card))
                candidates.Add(card);

        if (candidates.Count == 0)
        {
            _shuffleUsedCards.Clear();
            foreach (var item in tail)
                _shuffleUsedCards.Add(item);
            foreach (object? card in source)
                if (card is not null && passesFilter(card) && !_shuffleUsedCards.Contains(card))
                    candidates.Add(card);
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = _next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int needed = _poolSize - tail.Count;
        int take = Math.Min(needed, candidates.Count);

        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleQueue.AddRange(tail);
        for (int i = 0; i < take; i++)
        {
            _shuffleQueue.Add(candidates[i]);
            _shuffleUsedCards.Add(candidates[i]);
        }

        for (int i = 0; i < _shuffleQueue.Count; i++)
            _shuffleOrderMap[_shuffleQueue[i]] = i;
    }

    public void Clear()
    {
        _shuffleQueue.Clear();
        _shuffleOrderMap.Clear();
        _shuffleUsedCards.Clear();
    }

    public HashSet<object> GetDisplaySet(int displayLimit)
    {
        int displayCount = Math.Min(displayLimit, _shuffleQueue.Count);
        var displaySet = new HashSet<object>();
        for (int i = 0; i < displayCount; i++)
            displaySet.Add(_shuffleQueue[i]);
        return displaySet;
    }
}

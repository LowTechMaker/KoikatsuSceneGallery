namespace KoikatsuSceneGallery.Services;

public enum ThumbnailWorkPriority
{
    Prefetch,
    Visible
}

/// <summary>
/// Runs a bounded number of thumbnail jobs. Visible work is consumed before
/// prefetch work, and the newest work within either class is consumed first.
/// </summary>
public sealed class ThumbnailPriorityScheduler : IDisposable
{
    private const int DefaultCapacity = 128;
    private const int DefaultWorkerCount = 2;

    // These short queue-only critical sections are also entered during WinUI
    // container realization. Monitor waits on an STA pump COM/window messages,
    // which can reenter a protected XAML layout tick and fail fast (0xc000027b).
    // A SpinLock does not pump messages. Keep all work, callbacks and semaphore
    // operations outside it; do not make this mutable struct readonly.
    private SpinLock _sync = new(enableThreadOwnerTracking: false);
    private readonly LinkedList<WorkItem> _visible = [];
    private readonly LinkedList<WorkItem> _prefetch = [];
    private readonly Dictionary<long, LinkedListNode<WorkItem>> _pending = [];
    private readonly SemaphoreSlim _available = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly int _capacity;
    private long _nextId;
    private bool _disposed;

    public ThumbnailPriorityScheduler(
        int capacity = DefaultCapacity,
        int workerCount = DefaultWorkerCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);

        _capacity = capacity;
        for (var index = 0; index < workerCount; index++)
            _ = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Enqueues work without blocking the caller. Prefetch work is rejected when
    /// the queue is full; visible work evicts the oldest prefetch first.
    /// </summary>
    public ThumbnailWorkHandle? Enqueue(
        ThumbnailWorkPriority priority,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        Action? onDiscarded = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (cancellationToken.IsCancellationRequested) return null;

        WorkItem? discarded = null;
        var releaseSignal = true;
        ThumbnailWorkHandle? handle;
        var lockTaken = false;
        try
        {
            _sync.Enter(ref lockTaken);
            if (_disposed) return null;

            if (_pending.Count >= _capacity)
            {
                if (priority == ThumbnailWorkPriority.Prefetch)
                    return null;

                discarded = EvictOldestLocked(_prefetch.First is not null ? _prefetch : _visible);
                // The evicted item already had a signal; reuse it for the replacement.
                releaseSignal = false;
            }

            handle = new ThumbnailWorkHandle(++_nextId);
            var item = new WorkItem(handle.Value, action, cancellationToken, onDiscarded);
            var node = priority == ThumbnailWorkPriority.Visible
                ? _visible.AddFirst(item)
                : _prefetch.AddFirst(item);
            _pending.Add(handle.Value.Id, node);
        }
        finally
        {
            if (lockTaken) _sync.Exit();
        }

        if (releaseSignal)
            _available.Release();
        InvokeDiscarded(discarded);
        return handle;
    }

    /// <summary>Removes pending work in O(1). Running work is cancelled by its caller's token.</summary>
    public void Cancel(ThumbnailWorkHandle handle)
    {
        WorkItem? discarded = null;
        var lockTaken = false;
        try
        {
            _sync.Enter(ref lockTaken);
            if (_pending.Remove(handle.Id, out var node))
            {
                node.List!.Remove(node);
                discarded = node.Value;
            }
        }
        finally
        {
            if (lockTaken) _sync.Exit();
        }

        // A worker may have claimed the removed item's signal already; it can
        // consume another queued item or find no work and continue normally.
        if (discarded is not null) _available.Wait(0);

        InvokeDiscarded(discarded);
    }

    private WorkItem? EvictOldestLocked(LinkedList<WorkItem> queue)
    {
        var node = queue.Last;
        if (node is null) return null;

        queue.RemoveLast();
        _pending.Remove(node.Value.Handle.Id);
        return node.Value;
    }

    private async Task ConsumeAsync()
    {
        try
        {
            while (true)
            {
                await _available.WaitAsync(_shutdown.Token).ConfigureAwait(false);

                WorkItem? item;
                var lockTaken = false;
                try
                {
                    _sync.Enter(ref lockTaken);
                    item = TakeNextLocked();
                }
                finally
                {
                    if (lockTaken) _sync.Exit();
                }

                if (item is null)
                    continue;

                if (item.CancellationToken.IsCancellationRequested)
                {
                    InvokeDiscarded(item);
                    continue;
                }

                try
                {
                    await item.Action(item.CancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (item.CancellationToken.IsCancellationRequested)
                {
                    // Expected when the card leaves the realized range.
                }
                catch
                {
                    // Individual jobs log their failures. Keep the worker alive.
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Scheduler disposal ends worker loops.
        }
    }

    private static void InvokeDiscarded(WorkItem? item)
    {
        if (item?.OnDiscarded is not { } callback) return;
        try
        {
            callback();
        }
        catch
        {
            // Cleanup notifications must never stop a scheduler worker.
        }
    }

    private WorkItem? TakeNextLocked()
    {
        var node = _visible.First ?? _prefetch.First;
        if (node is null) return null;

        node.List!.Remove(node);
        _pending.Remove(node.Value.Handle.Id);
        return node.Value;
    }

    public void Dispose()
    {
        var lockTaken = false;
        try
        {
            _sync.Enter(ref lockTaken);
            if (_disposed) return;
            _disposed = true;
            _visible.Clear();
            _prefetch.Clear();
            _pending.Clear();
        }
        finally
        {
            if (lockTaken) _sync.Exit();
        }

        _shutdown.Cancel();
        // Workers may still be returning from WaitAsync. Leave these primitives alive
        // until process shutdown instead of racing them with Dispose on the UI thread.
    }

    private sealed record WorkItem(
        ThumbnailWorkHandle Handle,
        Func<CancellationToken, Task> Action,
        CancellationToken CancellationToken,
        Action? OnDiscarded);
}

public readonly record struct ThumbnailWorkHandle(long Id);

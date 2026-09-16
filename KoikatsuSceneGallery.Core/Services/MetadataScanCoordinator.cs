using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

internal delegate bool TryGetMetadata<TCard, TMetadata>(TCard card, out TMetadata metadata);

/// <summary>
/// Coordinates one library's metadata work without depending on UI types.
/// Start, Queue, Cancel, Dispose and dispatched callbacks run on the owner's UI thread.
/// Parsing runs on pipeline workers with a coordinator-owned token; restarting cancels
/// the previous scan. The returned task covers parsing and enqueueing, not UI completion.
/// The dispatcher must enqueue callbacks in order. Rejection and queued callback failures
/// are reported without throwing through the UI queue. Apply runs on the owner thread,
/// including for cache hits. Error reporting may run on workers or the owner thread and
/// must be thread-safe and not throw. Timers, filtering and indexing remain owner policies.
/// </summary>
internal sealed class MetadataScanCoordinator<TCard, TMetadata>(
    Func<TCard, bool> isLoaded,
    TryGetMetadata<TCard, TMetadata> tryGetCached,
    Func<TCard, CancellationToken, TMetadata> parse,
    Action<TCard, TMetadata> apply,
    Func<Action, bool> dispatch,
    Action<TCard, Exception> reportError,
    int concurrency = 4) : IDisposable
{
    private CancellationTokenSource? _cts;
    private readonly List<Task> _runs = [];
    private bool _disposed;
    public int PendingCount { get; private set; }
    public event Action<int>? PendingCountChanged;

    public Task Start(IEnumerable<TCard> cards, Action onEmpty, Action onStarted, Action onCompleted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetPendingCount(0);

        var pending = new List<TCard>();
        foreach (var card in cards)
        {
            if (isLoaded(card)) continue;
            if (tryGetCached(card, out var metadata))
                apply(card, metadata);
            else
                pending.Add(card);
        }

        if (pending.Count == 0)
        {
            onEmpty();
            return Task.CompletedTask;
        }

        SetPendingCount(pending.Count);
        onStarted();
        return Run(pending, token, onCompleted);
    }

    public Task Queue(TCard card, Action onCached, Action onCompleted)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (isLoaded(card)) return Task.CompletedTask;
        if (tryGetCached(card, out var metadata))
        {
            apply(card, metadata);
            onCached();
            return Task.CompletedTask;
        }

        _cts ??= new CancellationTokenSource();
        var token = _cts.Token;
        if (token.IsCancellationRequested) return Task.CompletedTask;
        SetPendingCount(PendingCount + 1);
        // Preserve the existing added-card pipeline, including its per-call concurrency.
        return Run([card], token, onCompleted);
    }

    private Task Run(IEnumerable<TCard> cards, CancellationToken token, Action onCompleted)
    {
        _runs.RemoveAll(task => task.IsCompleted);
        var run = BoundedAsyncPipeline.ForEachAsync(cards, concurrency,
            (card, cancellationToken) => ParseOneAsync(card, cancellationToken, token, onCompleted), token);
        _runs.Add(run);
        return run;
    }

    // Called on the owner thread. Stops new producers immediately, then waits for all
    // current and superseded workers. The caller's token limits only the wait.
    internal async Task StopAsync(CancellationToken cancellationToken)
    {
        Dispose();
        var pending = Task.WhenAll(_runs.ToArray());
        try { await pending.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Cancellation of the owned pipelines is expected during shutdown.
        }
    }

    private ValueTask ParseOneAsync(TCard card, CancellationToken cancellationToken, CancellationToken scanToken, Action onCompleted)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = parse(card, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
                DispatchCurrent(card, () => apply(card, metadata), scanToken, cancellationToken);
        }
        catch (Exception ex) { reportError(card, ex); }
        finally
        {
            DispatchCurrent(card, () =>
            {
                if (PendingCount > 0) SetPendingCount(PendingCount - 1);
                if (PendingCount == 0) onCompleted();
            }, scanToken, cancellationToken);
        }
        return ValueTask.CompletedTask;
    }

    private void DispatchCurrent(TCard card, Action publish, CancellationToken scanToken, CancellationToken workerToken)
    {
        if (scanToken.IsCancellationRequested || workerToken.IsCancellationRequested) return;
        try
        {
            if (!dispatch(() =>
            {
                // Keep the owner's token after the pipeline releases its linked token.
                if (scanToken.IsCancellationRequested || workerToken.IsCancellationRequested) return;
                try { publish(); }
                catch (Exception ex) { reportError(card, ex); }
            }))
                reportError(card, new InvalidOperationException("The UI dispatcher rejected metadata publication."));
        }
        catch (Exception ex) { reportError(card, ex); }
    }

    public void Cancel(bool resetCount = false)
    {
        _cts?.Cancel();
        if (resetCount) SetPendingCount(0);
    }

    private void SetPendingCount(int count)
    {
        PendingCount = count;
        PendingCountChanged?.Invoke(count);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}

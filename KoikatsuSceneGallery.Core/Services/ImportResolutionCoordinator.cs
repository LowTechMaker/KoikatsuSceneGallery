namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Serializes preparation and coalesces waiters until the newest result is published.
/// Preparation returns a UI-only commit closure. Caller cancellation cancels only its wait.
/// </summary>
internal sealed class ImportResolutionCoordinator<T>(
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private sealed record Request(Func<CancellationToken, Task<Func<T>>> Prepare, Func<Action, bool> Enqueue);
    private readonly object _gate = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private Request? _latest;
    private TaskCompletionSource<T>? _round;
    private CancellationTokenSource? _attempt;
    private long _version;
    private bool _running, _immediate;

    public Task<T> RequestAsync(Func<CancellationToken, Task<Func<T>>> prepare,
        Func<Action, bool> enqueue, bool debounce, CancellationToken callerToken = default)
    {
        callerToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _round ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            _latest = new(prepare, enqueue);
            _immediate |= !debounce;
            _version++;
            _attempt?.Cancel();
            var task = _round.Task;
            if (!_running)
            {
                _running = true;
                _ = Task.Run(RunAsync);
            }
            return task.WaitAsync(callerToken);
        }
    }

    public void CancelAll()
    {
        lock (_gate)
        {
            _version++;
            _latest = null;
            _round?.TrySetCanceled();
            _round = null;
            _immediate = false;
            _attempt?.Cancel();
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            Request request;
            TaskCompletionSource<T> round;
            CancellationTokenSource owner;
            long version;
            bool immediate;
            lock (_gate)
            {
                if (_latest is null) { _running = false; return; }
                request = _latest;
                round = _round!;
                version = _version;
                immediate = _immediate;
                owner = _attempt = new();
            }
            try
            {
                if (!immediate) await _delay(TimeSpan.FromMilliseconds(150), owner.Token).ConfigureAwait(false);
                owner.Token.ThrowIfCancellationRequested();
                var publish = await request.Prepare(owner.Token).ConfigureAwait(false);
                owner.Token.ThrowIfCancellationRequested();
                await AwaitedUiPublication.InvokeAsync(request.Enqueue, () =>
                {
                    lock (_gate)
                    {
                        if (version != _version) throw new OperationCanceledException(owner.Token);
                        var result = publish();
                        // A synchronous item notification may have requested another pass.
                        if (version == _version)
                        {
                            _latest = null;
                            _round = null;
                            _immediate = false;
                            round.TrySetResult(result);
                        }
                        return true;
                    }
                }, owner.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    if (version == _version)
                    {
                        _latest = null;
                        _round = null;
                        _immediate = false;
                        if (ex is OperationCanceledException) round.TrySetCanceled();
                        else round.TrySetException(ex);
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _attempt = null;
                    owner.Dispose();
                }
            }
        }
    }
}

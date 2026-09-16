namespace KoikatsuSceneGallery.Helpers;

// Protects a synchronous operation boundary without holding a lock during the work.
// Stop rejects new operations and waits for accepted ones, including faulted operations.
internal sealed class OperationDrain
{
    private readonly Lock _gate = new();
    private readonly TaskCompletionSource _drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _stopping;

    internal bool TryRun(Action operation)
    {
        lock (_gate)
        {
            if (_stopping) return false;
            _active++;
        }
        try { operation(); return true; }
        finally
        {
            lock (_gate)
            {
                _active--;
                if (_stopping && _active == 0) _drained.TrySetResult();
            }
        }
    }

    internal Task StopAsync()
    {
        lock (_gate)
        {
            _stopping = true;
            if (_active == 0) _drained.TrySetResult();
            return _drained.Task;
        }
    }
}

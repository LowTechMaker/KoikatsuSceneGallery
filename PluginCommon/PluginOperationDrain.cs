namespace SceneGallery.PluginCommon;

/// <summary>
/// Tracks complete producers, not individual waiters. Closing admission and cancelling work
/// precede bounded draining. Persistence is released only after both execution cleanup and
/// the last producer reach a terminal state. All owner callbacks run outside the state lock.
/// </summary>
internal sealed class PluginOperationDrain : IDisposable
{
    private readonly object _gate = new();
    private readonly string _ownerName;
    private readonly TimeSpan _disposeTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly Action _cancelActiveOperations;
    private readonly Func<DisposalDeadline, Task> _disposeExecution;
    private readonly Action _disposePersistence;
    private readonly Action<int> _logTimeout;
    private readonly Action<Exception>? _logFailure;
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _errors = [];
    private int _activeProducers;
    private bool _disposing;
    private bool _executionCompleted;
    private bool _persistenceClaimed;

    internal PluginOperationDrain(
        string ownerName,
        TimeSpan disposeTimeout,
        Action cancelActiveOperations,
        Func<DisposalDeadline, Task> disposeExecution,
        Action disposePersistence,
        Action<int> logTimeout,
        Action<Exception>? logFailure = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(disposeTimeout, TimeSpan.Zero);
        _ownerName = ownerName;
        _disposeTimeout = disposeTimeout;
        _cancelActiveOperations = cancelActiveOperations;
        _disposeExecution = disposeExecution;
        _disposePersistence = disposePersistence;
        _logTimeout = logTimeout;
        _logFailure = logFailure;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal bool IsDisposing { get { lock (_gate) return _disposing; } }

    /// <summary>Completes after actual cleanup, including deferred persistence; faults are observed.</summary>
    internal Task Completion => _completion.Task;

    internal async Task<T> RunProducerAsync<T>(Func<Task<T>> producer)
    {
        lock (_gate)
        {
            if (_disposing) throw new ObjectDisposedException(_ownerName);
            _activeProducers++;
        }
        try
        {
            return await producer().ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _activeProducers--;
                if (_activeProducers == 0) Monitor.PulseAll(_gate);
            }
            TryDisposePersistence();
        }
    }

    public void Dispose()
    {
        DisposalDeadline deadline;
        lock (_gate)
        {
            if (_disposing) return;
            deadline = new DisposalDeadline(_disposeTimeout, _timeProvider);
            _disposing = true;
        }

        try { _cancelActiveOperations(); }
        catch (Exception exception) { RecordFailure(exception); }

        int pending;
        lock (_gate)
        {
            while (_activeProducers > 0)
            {
                var remaining = deadline.Remaining;
                if (remaining == TimeSpan.Zero || !Monitor.Wait(_gate, remaining)) break;
            }
            pending = _activeProducers;
        }
        if (pending > 0)
        {
            try { _logTimeout(pending); }
            catch (Exception exception) { RecordFailure(exception); }
        }

        Task execution;
        try { execution = _disposeExecution(deadline); }
        catch (Exception exception) { execution = Task.FromException(exception); }
        // Observe before waiting: a timed-out caller must not orphan a cleanup failure.
        var observed = ObserveExecutionAsync(execution);
        deadline.Wait(observed);
    }

    private async Task ObserveExecutionAsync(Task execution)
    {
        try { await execution.ConfigureAwait(false); }
        catch (Exception exception) { RecordFailure(exception); }
        finally
        {
            lock (_gate) _executionCompleted = true;
            TryDisposePersistence();
        }
    }

    private void TryDisposePersistence()
    {
        lock (_gate)
        {
            if (!_disposing || !_executionCompleted || _activeProducers != 0 || _persistenceClaimed)
                return;
            _persistenceClaimed = true;
        }
        try { _disposePersistence(); }
        catch (Exception exception) { RecordFailure(exception); }
        finally
        {
            Exception[] errors;
            lock (_gate) errors = _errors.ToArray();
            if (errors.Length == 0) _completion.TrySetResult();
            else
            {
                _completion.TrySetException(errors);
                _ = _completion.Task.Exception;
            }
        }
    }

    private void RecordFailure(Exception exception)
    {
        lock (_gate) _errors.Add(exception);
        try { _logFailure?.Invoke(exception); }
        catch { /* Diagnostics must not prevent resource cleanup. */ }
    }
}

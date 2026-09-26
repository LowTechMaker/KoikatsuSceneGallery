namespace SceneGallery.PluginCommon;

/// <summary>One monotonic budget shared by every owned shutdown stage.</summary>
internal sealed class DisposalDeadline
{
    private readonly TimeProvider _timeProvider;
    private readonly long _started;
    private readonly TimeSpan _timeout;

    internal DisposalDeadline(TimeSpan timeout, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = timeout;
        _started = _timeProvider.GetTimestamp();
    }

    internal TimeSpan Remaining
    {
        get
        {
            var remaining = _timeout - _timeProvider.GetElapsedTime(_started);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Stops waiting at the deadline; it never cancels the owned cleanup task.</summary>
    internal bool Wait(Task task)
    {
        if (!task.IsCompleted)
        {
            var remaining = Remaining;
            if (remaining == TimeSpan.Zero) return false;
            try
            {
                task.WaitAsync(remaining, _timeProvider).GetAwaiter().GetResult();
            }
            catch (TimeoutException)
            {
                if (!task.IsCompleted) return false;
            }
        }
        task.GetAwaiter().GetResult();
        return true;
    }
}

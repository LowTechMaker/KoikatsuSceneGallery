namespace SceneGallery.PluginCommon;

internal sealed class DebouncedDiskPersistence : IDisposable
{
    private static readonly TimeSpan SaveDebounce = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly string _path;
    private readonly Action<Stream> _serialize;
    private readonly Action<Exception> _logSaveFailure;
    private readonly Action<string, Action<Stream>> _writeAtomically;
    private readonly Timer _saveTimer;
    private readonly Lock _saveLock = new();
    private bool _dirty;
    private bool _disposed;
    private long _generation;
    private TimeSpan _retryDelay;

    internal DebouncedDiskPersistence(
        string path,
        Action<Stream> serialize,
        Action<Exception> logSaveFailure)
        : this(path, serialize, logSaveFailure, AtomicFileWriter.Write)
    {
    }

    internal DebouncedDiskPersistence(
        string path,
        Action<Stream> serialize,
        Action<Exception> logSaveFailure,
        Action<string, Action<Stream>> writeAtomically)
    {
        _path = path;
        _serialize = serialize;
        _logSaveFailure = logSaveFailure;
        _writeAtomically = writeAtomically;
        _saveTimer = new Timer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    internal bool IsDirty
    {
        get
        {
            lock (_saveLock)
                return _dirty;
        }
    }

    internal TimeSpan RetryDelay
    {
        get
        {
            lock (_saveLock)
                return _retryDelay;
        }
    }

    internal void MarkDirty()
    {
        Interlocked.Increment(ref _generation);
        lock (_saveLock)
        {
            _dirty = true;
            ScheduleFlush(SaveDebounce);
        }
    }

    internal void Flush()
    {
        lock (_saveLock)
        {
            if (!_dirty) return;

            var generation = Volatile.Read(ref _generation);
            try
            {
                _writeAtomically(_path, _serialize);
                _retryDelay = TimeSpan.Zero;
                if (generation == Volatile.Read(ref _generation))
                {
                    _dirty = false;
                }
                else
                {
                    _dirty = true;
                    ScheduleFlush(SaveDebounce);
                }
            }
            catch (Exception ex)
            {
                _dirty = true;
                _retryDelay = _retryDelay == TimeSpan.Zero
                    ? InitialRetryDelay
                    : TimeSpan.FromTicks(Math.Min(_retryDelay.Ticks * 2, MaxRetryDelay.Ticks));
                ScheduleFlush(_retryDelay);
                _logSaveFailure(ex);
            }
        }
    }

    private void ScheduleFlush(TimeSpan delay)
    {
        if (!_disposed)
            _saveTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_saveLock)
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        Flush();
        _saveTimer.Dispose();
    }
}

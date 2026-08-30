namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Tracks thumbnail cache work that is in progress across every gallery page.
/// </summary>
public sealed class ThumbnailCacheActivity
{
    private int _activeWorkCount;
    private readonly CancellationTokenSource _shutdownCts = new();

    public int ActiveWorkCount => Volatile.Read(ref _activeWorkCount);

    public bool IsCaching => ActiveWorkCount > 0;

    /// <summary>
    /// Cancels all thumbnail cache work when the application is closing.
    /// </summary>
    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public event Action<int>? ActiveWorkCountChanged;

    public IDisposable Begin()
    {
        NotifyCountChanged(Interlocked.Increment(ref _activeWorkCount));
        return new ActivityLease(this);
    }

    public void CancelForShutdown() => _shutdownCts.Cancel();

    private void Complete()
    {
        var count = Interlocked.Decrement(ref _activeWorkCount);
        NotifyCountChanged(count);
    }

    private void NotifyCountChanged(int count)
        => ActiveWorkCountChanged?.Invoke(count);

    private sealed class ActivityLease(ThumbnailCacheActivity owner) : IDisposable
    {
        private ThumbnailCacheActivity? _owner = owner;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Complete();
        }
    }
}

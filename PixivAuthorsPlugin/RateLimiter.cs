using System.Diagnostics;

namespace SceneGallery.Plugin.PixivAuthors;

/// <summary>
/// Serializes all outgoing HTTP and enforces a minimum gap between consecutive
/// requests. Every request — JSON and avatar downloads alike — must pass
/// through this so the plugin can never burst-traffic pixiv.
/// </summary>
internal sealed class RateLimiter(TimeSpan minInterval)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastRequestTimestamp;

    public async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var last = Interlocked.Read(ref _lastRequestTimestamp);
            if (last != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(last);
                var remaining = minInterval - elapsed;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            _gate.Release();
            throw;
        }
        return new Releaser(this);
    }

    private sealed class Releaser(RateLimiter owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            // Stamp on release so the interval is measured from when the
            // request finished, not when it started.
            Interlocked.Exchange(ref owner._lastRequestTimestamp, Stopwatch.GetTimestamp());
            owner._gate.Release();
        }
    }
}

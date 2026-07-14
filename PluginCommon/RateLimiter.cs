using System.Diagnostics;

namespace SceneGallery.PluginCommon;

internal sealed class RateLimiter(TimeSpan minInterval, TimeSpan maxJitter = default)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _lastRequestTimestamp;

    internal async Task<IDisposable> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var last = Interlocked.Read(ref _lastRequestTimestamp);
            if (last != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(last);
                var jitter = maxJitter > TimeSpan.Zero
                    ? TimeSpan.FromMilliseconds(Random.Shared.Next((int)maxJitter.TotalMilliseconds))
                    : TimeSpan.Zero;
                var remaining = minInterval + jitter - elapsed;
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
            Interlocked.Exchange(ref owner._lastRequestTimestamp, Stopwatch.GetTimestamp());
            owner._gate.Release();
        }
    }
}

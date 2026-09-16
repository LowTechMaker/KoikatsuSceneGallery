using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class CacheLoadGateTests
{
    [Fact]
    public void OwnerCanRetryMissingDataAndFinalizeHandledFailure()
    {
        var gate = new CacheLoadGate();
        int attempts = 0;
        gate.EnsureLoaded(() => { attempts++; return false; });
        gate.EnsureLoaded(() => { attempts++; return true; });
        gate.EnsureLoaded(() => throw new Exception("already loaded"));
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void UnhandledFailureDoesNotPublishCompletion()
    {
        var gate = new CacheLoadGate();
        var failure = new IOException("load failed");
        Assert.Same(failure, Assert.Throws<IOException>(() => gate.EnsureLoaded(() => throw failure)));
        bool retried = false;
        gate.EnsureLoaded(() => { retried = true; return true; });
        Assert.True(retried);
    }

    [Fact]
    public async Task ParallelReadersShareOneCompletedLoad()
    {
        var gate = new CacheLoadGate();
        int attempts = 0, value = 0;
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int LoadAndRead()
        {
            gate.EnsureLoaded(() =>
            {
                Interlocked.Increment(ref attempts);
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
                value = 42;
                return true;
            });
            return value;
        }
        var first = Task.Run(LoadAndRead);
        Task<int>[] readers = [];
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            readers = Enumerable.Range(0, 8).Select(_ => Task.Run(LoadAndRead)).ToArray();
        }
        finally
        {
            release.Set();
            Assert.Equal(42, await first.WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.All(await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(15)), item => Assert.Equal(42, item));
        }
        Assert.Equal(1, attempts);
    }
}

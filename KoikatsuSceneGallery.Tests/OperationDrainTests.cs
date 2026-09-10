using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class OperationDrainTests
{
    [Fact]
    public async Task StopRejectsNewWorkAndWaitsForAcceptedOperation()
    {
        var drain = new OperationDrain();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var worker = Task.Run(() => drain.TryRun(() =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
        }));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var stopping = drain.StopAsync();
            Assert.False(stopping.IsCompleted);
            Assert.Same(stopping, drain.StopAsync());
            Assert.False(drain.TryRun(() => throw new Exception("must not execute")));
        }
        finally
        {
            release.Set();
            Assert.True(await worker.WaitAsync(TimeSpan.FromSeconds(15)));
            await drain.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact]
    public async Task FaultedOperationReleasesLeaseAndStopIsPermanent()
    {
        var drain = new OperationDrain();
        var failure = new IOException("operation failed");
        Assert.Same(failure, Assert.Throws<IOException>(() => drain.TryRun(() => throw failure)));
        Assert.True(drain.TryRun(() => { }));
        await drain.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(drain.TryRun(() => throw new Exception("must not execute")));
    }
}

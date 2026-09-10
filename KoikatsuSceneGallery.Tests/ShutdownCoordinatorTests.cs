using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ShutdownCoordinatorTests
{
    [Fact]
    public async Task TimeoutKeepsOneDrainAndDefersEachDisposalUntilItsWorkerFinishes()
    {
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstDisposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int starts = 0, disposed = 0;
        var coordinator = new ShutdownCoordinator(
        [
            new(() => { starts++; return first.Task; }, () =>
            {
                Interlocked.Increment(ref disposed);
                firstDisposed.SetResult();
            }),
            new(() => { starts++; return second.Task; }, () => Interlocked.Increment(ref disposed))
        ], error => throw new Xunit.Sdk.XunitException(error.ToString()));
        try
        {
            Assert.False(await coordinator.WaitAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Equal(2, starts);
            Assert.Equal(0, disposed);
            var original = coordinator.Start();
            Assert.Same(original, coordinator.Start());
            first.SetResult();
            await firstDisposed.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, disposed);
            Assert.False(original.IsCompleted);
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
            await coordinator.Start().WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.Equal(2, disposed);
        Assert.True(await coordinator.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(2, starts);
    }

    [Fact]
    public async Task FailedDrainSkipsUnsafeDisposalButDoesNotSkipOtherOwners()
    {
        var failure = new IOException("cannot drain");
        var errors = new List<Exception>();
        var disposed = false;
        var coordinator = new ShutdownCoordinator(
        [
            new(() => throw failure, () => throw new Exception("unsafe disposal")),
            new(() => Task.CompletedTask, () => disposed = true)
        ], errors.Add);
        await coordinator.Start().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(disposed);
        Assert.Same(failure, Assert.Single(errors));
    }
}

using SceneGallery.PluginCommon;

namespace SceneGallery.PluginCommon.Tests;

public sealed class PluginOperationDrainTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task ShutdownCancelsBeforeExecutionAndPersistence_RejectsNewProducers()
    {
        var order = new List<string>();
        using var shutdown = new CancellationTokenSource();
        var drain = new PluginOperationDrain("test", TimeSpan.FromSeconds(2),
            () => { order.Add("cancel"); shutdown.Cancel(); },
            _ => { order.Add("execution"); return Task.CompletedTask; },
            () => order.Add("persistence"), _ => throw new Exception("unexpected timeout"));
        var producer = drain.RunProducerAsync(async () =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, shutdown.Token);
            return 0;
        });
        await Task.Run(drain.Dispose);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => producer);
        await drain.Completion;
        await Assert.ThrowsAsync<ObjectDisposedException>(() => drain.RunProducerAsync(() => Task.FromResult(1)));
        drain.Dispose();
        Assert.Equal(["cancel", "execution", "persistence"], order);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TimeoutDefersPersistenceUntilBothCleanupAndLastProducerFinish(bool cleanupFirst)
    {
        var cleanup = Signal();
        var release = Signal();
        var count = 0;
        var timedOut = 0;
        var drain = new PluginOperationDrain("test", TimeSpan.Zero, () => { },
            deadline => { Assert.Equal(TimeSpan.Zero, deadline.Remaining); return cleanup.Task; },
            () => Interlocked.Increment(ref count), pending => timedOut = pending);
        var producer = drain.RunProducerAsync(async () => { await release.Task; return 42; });
        drain.Dispose();
        Assert.Equal(1, timedOut);
        Assert.False(drain.Completion.IsCompleted);
        if (cleanupFirst)
        {
            cleanup.SetResult();
            Assert.Equal(0, count);
            release.SetResult();
        }
        else
        {
            release.SetResult();
            Assert.Equal(42, await producer);
            Assert.Equal(0, count);
            cleanup.SetResult();
        }
        Assert.Equal(42, await producer);
        await drain.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        drain.Dispose();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task CleanupFailureAfterTimeoutIsObservedAndPersistenceStillRuns()
    {
        var cleanup = Signal();
        var errors = new List<Exception>();
        var persisted = false;
        var drain = new PluginOperationDrain("test", TimeSpan.Zero, () => { }, _ => cleanup.Task,
            () => persisted = true, _ => { }, errors.Add);
        drain.Dispose();
        cleanup.SetException(new InvalidOperationException("cleanup"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drain.Completion);
        Assert.True(persisted);
        Assert.Single(errors);
    }

    [Fact]
    public async Task CallbacksAreOutsideOperationLock_AndFailuresDoNotPreventCleanup()
    {
        PluginOperationDrain drain = null!;
        var execution = false;
        var persistence = false;
        void CheckLock() => Task.Run(() => Assert.True(drain.IsDisposing)).WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        drain = new PluginOperationDrain("test", TimeSpan.Zero,
            () => { CheckLock(); throw new InvalidOperationException("cancel"); },
            _ => { CheckLock(); execution = true; return Task.CompletedTask; },
            () => { CheckLock(); persistence = true; }, _ => { });
        drain.Dispose();
        await Assert.ThrowsAsync<InvalidOperationException>(() => drain.Completion);
        Assert.True(execution && persistence);
    }

    [Fact]
    public async Task ThrowingProducerStillReleasesItsLease()
    {
        var disposed = false;
        var drain = new PluginOperationDrain("test", TimeSpan.Zero, () => { }, _ => Task.CompletedTask,
            () => disposed = true, _ => throw new Exception("leaked producer"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => drain.RunProducerAsync<int>(() => throw new InvalidOperationException()));
        drain.Dispose();
        await drain.Completion;
        Assert.True(disposed);
    }

    [Fact]
    public void DeadlineIsSharedAcrossStages_AndExpiredWaitDoesNotCancelCleanup()
    {
        var clock = new ManualClock();
        var deadline = new DisposalDeadline(TimeSpan.FromSeconds(10), clock);
        clock.Advance(TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(3), deadline.Remaining);
        Assert.True(deadline.Wait(Task.CompletedTask));
        clock.Advance(TimeSpan.FromSeconds(4));
        var cleanup = Signal();
        Assert.Equal(TimeSpan.Zero, deadline.Remaining);
        Assert.False(deadline.Wait(cleanup.Task));
        Assert.False(cleanup.Task.IsCanceled);
    }

    [Fact]
    public async Task CancellationTimeIsIncludedInExecutionBudget()
    {
        var clock = new ManualClock();
        var drain = new PluginOperationDrain("test", TimeSpan.FromSeconds(10),
            () => clock.Advance(TimeSpan.FromSeconds(7)),
            deadline => { Assert.Equal(TimeSpan.FromSeconds(3), deadline.Remaining); return Task.CompletedTask; },
            () => { }, _ => { }, timeProvider: clock);
        drain.Dispose();
        await drain.Completion;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        internal void Advance(TimeSpan time) => _timestamp += time.Ticks;
    }
}

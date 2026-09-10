using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class BlockingUiPublicationTests
{
    [Fact]
    public async Task ProducerWaitsForPublication()
    {
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = false;
        var producer = Task.Run(() => AwaitedUiPublication.InvokeBlocking(
            action => { queued.SetResult(action); return true; }, () => applied = true,
            TimeSpan.FromSeconds(10), default));
        var callback = await queued.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(producer.IsCompleted);
        Assert.False(applied);
        callback();
        await producer.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(applied);
    }

    [Fact]
    public void TimeoutInvalidatesQueuedPublication()
    {
        Action callback = null!;
        var applied = false;
        Assert.Throws<TimeoutException>(() => AwaitedUiPublication.InvokeBlocking(
            action => { callback = action; return true; }, () => applied = true, TimeSpan.Zero, default));
        callback();
        Assert.False(applied);
    }

    [Fact]
    public async Task CallerCancellationInvalidatesQueuedPublication()
    {
        using var cancel = new CancellationTokenSource();
        var queued = new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = false;
        var producer = Task.Run(() => AwaitedUiPublication.InvokeBlocking(
            action => { queued.SetResult(action); return true; }, () => applied = true,
            TimeSpan.FromSeconds(10), cancel.Token));
        var callback = await queued.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => producer.WaitAsync(TimeSpan.FromSeconds(15)));
        callback();
        Assert.False(applied);
    }

    [Fact]
    public void RejectionFailsImmediatelyAndNextPublicationSucceeds()
    {
        Assert.Throws<InvalidOperationException>(() => AwaitedUiPublication.InvokeBlocking(
            _ => false, () => throw new Exception("must not run"), TimeSpan.Zero, default));
        var applied = false;
        AwaitedUiPublication.InvokeBlocking(action => { action(); return true; },
            () => applied = true, TimeSpan.FromSeconds(10), default);
        Assert.True(applied);
    }

    [Fact]
    public void PublisherExceptionReachesProducer()
    {
        var error = new IOException("publication failed");
        Assert.Same(error, Assert.Throws<IOException>(() => AwaitedUiPublication.InvokeBlocking(
            action => { action(); return true; }, () => throw error, TimeSpan.FromSeconds(10), default)));
    }
}

using System.Threading.Channels;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportResolutionCoordinatorTests
{
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private sealed class UiQueue
    {
        private readonly Channel<Action> _queue = Channel.CreateUnbounded<Action>();
        public bool Enqueue(Action action) => _queue.Writer.TryWrite(action);
        public async Task<Action> Next() => await _queue.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public async Task RunNext() => (await Next())();
    }

    [Fact]
    public async Task OldUncancelableCalculationFinishesBeforeNewestAndAllWaitersGetNewest()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var started = Signal<bool>();
        var finish = Signal<Func<int>>();
        var secondStarted = Signal<bool>();
        var oldPublished = false;
        var first = coordinator.RequestAsync(_ => { started.SetResult(true); return finish.Task; }, ui.Enqueue, false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.RequestAsync(_ =>
        {
            secondStarted.SetResult(true);
            return Task.FromResult<Func<int>>(() => 2);
        }, ui.Enqueue, false);
        Assert.False(secondStarted.Task.IsCompleted);
        finish.SetResult(() => { oldPublished = true; return 1; });
        await ui.RunNext();
        Assert.Equal(2, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, await second);
        Assert.False(oldPublished);
    }

    [Fact]
    public async Task QueuedOldPublicationCannotOverwriteNewResult()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var published = 0;
        var first = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => ++published), ui.Enqueue, false);
        var oldCallback = await ui.Next();
        var second = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => { published = 20; return 20; }), ui.Enqueue, false);
        oldCallback();
        await ui.RunNext();
        Assert.Equal(20, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(20, await second);
        Assert.Equal(20, published);
    }

    [Fact]
    public async Task DebounceRestartsAndImmediatePrioritySurvivesLaterDebounce()
    {
        var delays = Channel.CreateUnbounded<TaskCompletionSource<bool>>();
        var coordinator = new ImportResolutionCoordinator<int>(async (duration, token) =>
        {
            Assert.Equal(TimeSpan.FromMilliseconds(150), duration);
            var gate = Signal<bool>();
            delays.Writer.TryWrite(gate);
            await gate.Task.WaitAsync(token);
        });
        var ui = new UiQueue();
        var first = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 1), ui.Enqueue, true);
        await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var second = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 2), ui.Enqueue, true);
        await delays.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var started = Signal<bool>();
        var hold = Signal<Func<int>>();
        var immediate = coordinator.RequestAsync(_ => { started.SetResult(true); return hold.Task; }, ui.Enqueue, false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var last = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 4), ui.Enqueue, true);
        hold.SetResult(() => 3);
        await ui.RunNext();
        Assert.False(delays.Reader.TryRead(out _));
        Assert.Equal(new[] { 4, 4, 4, 4 }, await Task.WhenAll(first, second, immediate, last).WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CallerCancellationOnlyCancelsItsOwnWait()
    {
        using var caller = new CancellationTokenSource();
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var first = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 1), ui.Enqueue, false, caller.Token);
        var stale = await ui.Next();
        var second = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 2), ui.Enqueue, false);
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        stale();
        await ui.RunNext();
        Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ClearCancelsRoundInvalidatesUiAndAllowsNextRound()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var first = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => throw new Exception("stale")), ui.Enqueue, false);
        var stale = await ui.Next();
        coordinator.CancelAll();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var next = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 3), ui.Enqueue, false);
        stale();
        await ui.RunNext();
        Assert.Equal(3, await next.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ClearDoesNotStartNewBackgroundUntilOldBackgroundDrains()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var started = Signal<bool>();
        var hold = Signal<Func<int>>();
        var old = coordinator.RequestAsync(_ => { started.SetResult(true); return hold.Task; }, ui.Enqueue, false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.CancelAll();
        var nextStarted = false;
        var next = coordinator.RequestAsync(_ => { nextStarted = true; return Task.FromResult<Func<int>>(() => 2); }, ui.Enqueue, false);
        Assert.False(nextStarted);
        hold.SetResult(() => 1);
        await ui.RunNext();
        Assert.Equal(2, await next.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => old);
    }

    [Fact]
    public async Task FailuresAndDispatcherRejectionReachWaitersAndAllowRetry()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        await Assert.ThrowsAsync<IOException>(() => coordinator.RequestAsync(_ => throw new IOException("expected"), _ => true, false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 1), _ => false, false));
        var ui = new UiQueue();
        var failedPublish = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => throw new IOException("publish")), ui.Enqueue, false);
        await ui.RunNext();
        await Assert.ThrowsAsync<IOException>(() => failedPublish);
        var retry = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 7), ui.Enqueue, false);
        await ui.RunNext();
        Assert.Equal(7, await retry.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ReentrantRequestDuringPublishWaitsForFollowingPass()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        Task<int>? newer = null;
        var first = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() =>
        {
            newer = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 2), ui.Enqueue, false);
            return 1;
        }), ui.Enqueue, false);
        await ui.RunNext();
        await ui.RunNext();
        Assert.Equal(2, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, await newer!);
    }

    [Fact]
    public async Task AwaitedPublicationWaitsAndCanceledBatchDoesNotAffectAnotherBatch()
    {
        var ui = new UiQueue();
        using var firstToken = new CancellationTokenSource();
        var published = new List<int>();
        var first = AwaitedUiPublication.InvokeAsync(ui.Enqueue, () => { published.Add(1); return 1; }, firstToken.Token);
        var second = AwaitedUiPublication.InvokeAsync(ui.Enqueue, () => { published.Add(2); return 2; }, default);
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        firstToken.Cancel();
        await ui.RunNext();
        await ui.RunNext();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(2, await second);
        Assert.Equal(new[] { 2 }, published);
    }

    [Fact]
    public async Task SupersededFailureDoesNotFailLatestWaiters()
    {
        var coordinator = new ImportResolutionCoordinator<int>();
        var ui = new UiQueue();
        var started = Signal<bool>();
        var failure = Signal<Func<int>>();
        var first = coordinator.RequestAsync(_ => { started.SetResult(true); return failure.Task; }, ui.Enqueue, false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = coordinator.RequestAsync(_ => Task.FromResult<Func<int>>(() => 9), ui.Enqueue, false);
        failure.SetException(new IOException("obsolete"));
        await ui.RunNext();
        Assert.Equal(9, await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(9, await latest);
    }

    [Fact]
    public async Task PublicationFailureAndCancellationAfterSuccessAreBounded()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AwaitedUiPublication.InvokeAsync(_ => false, () => 1, default));
        var ui = new UiQueue();
        using var token = new CancellationTokenSource();
        var first = AwaitedUiPublication.InvokeAsync(ui.Enqueue, () => 1, token.Token);
        var second = AwaitedUiPublication.InvokeAsync(ui.Enqueue, () => 2, default);
        await ui.RunNext();
        await ui.RunNext();
        Assert.Equal(new[] { 1, 2 }, await Task.WhenAll(first, second));
        token.Cancel();
        Assert.Equal(1, await first);
        var error = AwaitedUiPublication.InvokeAsync<int>(ui.Enqueue, () => throw new IOException("publish"), default);
        await ui.RunNext();
        await Assert.ThrowsAsync<IOException>(() => error);
    }
}

using System.Collections.Concurrent;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class MetadataScanCoordinatorTests
{
    private sealed class Card { public bool Loaded; public int Value; }
    private sealed class Harness : IDisposable
    {
        public readonly ConcurrentQueue<Action> Ui = new();
        public readonly ConcurrentQueue<Exception> Errors = new();
        public readonly Dictionary<Card, int> Cache = new();
        public Func<Card, CancellationToken, int> Parse = (_, _) => 42;
        public Func<Action, bool>? Dispatch;
        public Action? BeforeApply, BeforeComplete;
        public int Completed, Started, Empty, Applied;
        public readonly MetadataScanCoordinator<Card, int> Scan;
        public Harness()
        {
            Scan = new(c => c.Loaded, Cache.TryGetValue, (c, t) => Parse(c, t),
                (c, v) => { BeforeApply?.Invoke(); c.Loaded = true; c.Value = v; Applied++; },
                a => { if (Dispatch is not null) return Dispatch(a); Ui.Enqueue(a); return true; }, (_, e) => Errors.Enqueue(e));
        }
        public Task Start(params Card[] cards) => Scan.Start(cards, () => Empty++, () => Started++, () => { BeforeComplete?.Invoke(); Completed++; });
        public void Drain() { while (Ui.TryDequeue(out var action)) action(); }
        public void Dispose() => Scan.Dispose();
    }

    [Fact]
    public async Task MixedCacheAndParsedCardsApplyBeforeCompletion()
    {
        using var h = new Harness();
        var cached = new Card(); var parsed = new Card();
        h.Cache[cached] = 7;
        await h.Start(cached, parsed, new Card { Loaded = true });
        Assert.Equal(7, cached.Value);
        Assert.False(parsed.Loaded);
        Assert.Equal(1, h.Scan.PendingCount);
        h.Drain();
        Assert.Equal(42, parsed.Value);
        Assert.Equal(2, h.Applied);
        Assert.Equal(1, h.Completed);
        Assert.Equal(0, h.Scan.PendingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedOrThrowingDispatcherReportsBothPublicationsAndRestartWorks(bool throws)
    {
        using var h = new Harness();
        var failure = new IOException("dispatcher failed");
        h.Dispatch = _ => throws ? throw failure : false;
        var card = new Card();
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.False(card.Loaded);
        Assert.Equal(2, h.Errors.Count); // metadata and completion are separate publications.
        if (throws) Assert.All(h.Errors, error => Assert.Same(failure, error));
        else Assert.All(h.Errors, error => Assert.IsType<InvalidOperationException>(error));
        h.Dispatch = null;
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        h.Drain();
        Assert.True(card.Loaded);
        Assert.Equal(0, h.Scan.PendingCount);
        Assert.Equal(1, h.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UiApplyOrCompletionFailureIsReportedWithoutEscapingQueue(bool failsCompletion)
    {
        using var h = new Harness();
        var failure = new IOException("UI callback failed");
        if (failsCompletion) h.BeforeComplete = () => throw failure;
        else h.BeforeApply = () => throw failure;
        var card = new Card();
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        h.Drain();
        Assert.Same(failure, Assert.Single(h.Errors));
        Assert.Equal(failsCompletion, card.Loaded);
        Assert.Equal(0, h.Scan.PendingCount);
        h.BeforeApply = h.BeforeComplete = null;
        await h.Start(new Card()).WaitAsync(TimeSpan.FromSeconds(15));
        h.Drain();
        Assert.Equal(0, h.Scan.PendingCount);
        Assert.Equal(failsCompletion ? 1 : 2, h.Completed);
    }

    [Fact]
    public async Task CacheOnlyScanUsesEmptyPolicyAndQueueUsesCachedPolicy()
    {
        using var h = new Harness();
        var card = new Card(); h.Cache[card] = 7;
        await h.Start(card);
        Assert.Equal(1, h.Empty); Assert.Equal(0, h.Started);
        card.Loaded = false;
        var cached = 0;
        await h.Scan.Queue(card, () => cached++, () => h.Completed++);
        Assert.Equal(1, cached); Assert.Equal(0, h.Completed);
    }

    [Fact]
    public async Task OldCompletionCannotDecrementNewScan()
    {
        using var h = new Harness();
        await h.Start(new Card()); // UI callbacks deliberately remain queued.
        var oldCallbacks = h.Ui.ToArray();
        h.Ui.Clear();
        await h.Start(new Card());
        foreach (var callback in oldCallbacks) callback();
        Assert.Equal(1, h.Scan.PendingCount);
        Assert.Equal(0, h.Completed);
        h.Drain();
        Assert.Equal(0, h.Scan.PendingCount);
        Assert.Equal(1, h.Completed);
    }

    [Fact]
    public async Task FailuresReportAndCompleteWithoutMarkingCardLoaded()
    {
        using var h = new Harness();
        h.Parse = (_, _) => throw new InvalidDataException();
        var card = new Card();
        await h.Start(card); h.Drain();
        Assert.False(card.Loaded); Assert.Single(h.Errors);
        Assert.Equal(0, h.Scan.PendingCount); Assert.Equal(1, h.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrDisposeInvalidatesQueuedMetadata(bool dispose)
    {
        using var h = new Harness();
        var card = new Card();
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        if (dispose) h.Scan.Dispose();
        else h.Scan.Cancel(resetCount: true);
        h.Drain();
        Assert.False(card.Loaded);
        Assert.Equal(0, h.Applied);
        Assert.Equal(0, h.Completed);
    }

    [Fact]
    public async Task OldMetadataCannotOverwriteRestartedScanOfSameCard()
    {
        using var h = new Harness();
        var card = new Card();
        h.Parse = (_, _) => 1;
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        var oldCallbacks = h.Ui.ToArray();
        h.Ui.Clear();
        h.Parse = (_, _) => 2;
        await h.Start(card).WaitAsync(TimeSpan.FromSeconds(15));
        h.Drain();
        foreach (var callback in oldCallbacks) callback();
        Assert.Equal(2, card.Value);
        Assert.Equal(1, h.Applied);
        Assert.Equal(1, h.Completed);
        Assert.Equal(0, h.Scan.PendingCount);
    }

    [Fact]
    public async Task CancelStopsNewUncachedQueueAndRestartWorks()
    {
        using var h = new Harness();
        await h.Start(new Card());
        h.Scan.Cancel(resetCount: true); h.Drain();
        var card = new Card();
        await h.Scan.Queue(card, () => { }, () => h.Completed++);
        Assert.False(card.Loaded); Assert.Equal(0, h.Completed);
        await h.Start(card); h.Drain();
        Assert.True(card.Loaded);
        h.Scan.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Start(new Card()));
    }

    [Fact]
    public async Task BatchConcurrencyIsBoundedAndAllCardsFinish()
    {
        using var h = new Harness();
        using var fourWorkers = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        int active = 0, maximum = 0, entered = 0;
        h.Parse = (_, token) =>
        {
            int current = Interlocked.Increment(ref active);
            int old;
            do { old = maximum; } while (current > old && Interlocked.CompareExchange(ref maximum, current, old) != old);
            if (Interlocked.Increment(ref entered) <= 4) fourWorkers.Signal();
            try { release.Wait(token); return 42; }
            finally { Interlocked.Decrement(ref active); }
        };
        var work = h.Start(Enumerable.Range(0, 20).Select(_ => new Card()).ToArray());
        try { Assert.True(await Task.Run(() => fourWorkers.Wait(TimeSpan.FromSeconds(15)))); }
        finally { release.Set(); await work; }
        h.Drain();
        Assert.Equal(4, maximum); Assert.Equal(20, h.Applied);
        Assert.Equal(0, h.Scan.PendingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopWaitsForSupersededWorkerEvenWhenItIgnoresCancellation(bool cancelWait)
    {
        using var h = new Harness();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        h.Parse = (_, _) =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            return 42;
        };
        var oldRun = h.Start(new Card());
        Task? stopping = null;
        try
        {
            Assert.True(await Task.Run(() => entered.Wait(TimeSpan.FromSeconds(15))));
            await h.Start().WaitAsync(deadline.Token); // Cancels but does not finish old worker.
            stopping = h.Scan.StopAsync(deadline.Token);
            Assert.False(stopping.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => h.Start(new Card()));
            if (cancelWait)
            {
                deadline.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopping);
                Assert.False(oldRun.IsCompleted);
            }
        }
        finally
        {
            release.Set();
            try { await oldRun.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            // A canceled waiter must not discard tracking or prevent a later drain.
            await h.Scan.StopAsync(default).WaitAsync(TimeSpan.FromSeconds(15));
            if (stopping is not null && !cancelWait)
                await stopping.WaitAsync(TimeSpan.FromSeconds(15));
        }
        h.Drain();
        Assert.Equal(0, h.Applied);
    }
}

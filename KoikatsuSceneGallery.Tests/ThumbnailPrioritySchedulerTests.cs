using System.Collections.Concurrent;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public class ThumbnailPrioritySchedulerTests
{
    [Fact]
    public async Task VisibleWorkEvictsOldestPrefetchAndRunsFirst()
    {
        using var scheduler = new ThumbnailPriorityScheduler(capacity: 2, workerCount: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        scheduler.Enqueue(ThumbnailWorkPriority.Visible, async _ =>
        {
            started.SetResult();
            await release.Task;
        }, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var discarded = 0;
            scheduler.Enqueue(ThumbnailWorkPriority.Prefetch, _ =>
            {
                order.Enqueue("old");
                return Task.CompletedTask;
            }, default, () => Interlocked.Increment(ref discarded));
            scheduler.Enqueue(ThumbnailWorkPriority.Prefetch, _ =>
            {
                order.Enqueue("prefetch");
                done.SetResult();
                return Task.CompletedTask;
            }, default);
            scheduler.Enqueue(ThumbnailWorkPriority.Visible, _ =>
            {
                order.Enqueue("visible");
                return Task.CompletedTask;
            }, default);
            Assert.Equal(1, discarded);
            release.SetResult();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "visible", "prefetch" }, order.ToArray());
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task CancelRemovesQueuedWorkAndAllowsReplacement()
    {
        using var scheduler = new ThumbnailPriorityScheduler(capacity: 1, workerCount: 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Enqueue(ThumbnailWorkPriority.Visible, async _ =>
        {
            started.SetResult();
            await release.Task;
        }, default);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            int ran = 0, discarded = 0;
            var handle = scheduler.Enqueue(ThumbnailWorkPriority.Visible, _ =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            }, default, () => Interlocked.Increment(ref discarded));
            scheduler.Cancel(handle!.Value);
            scheduler.Cancel(handle.Value);
            Assert.Equal(1, discarded);
            Assert.NotNull(scheduler.Enqueue(ThumbnailWorkPriority.Prefetch, _ =>
            {
                done.SetResult();
                return Task.CompletedTask;
            }, default));
            release.SetResult();
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, ran);
        }
        finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task ContendedQueueCompletesOrDiscardsEveryAcceptedJobExactlyOnce()
    {
        const int jobCount = 20000;
        using var scheduler = new ThumbnailPriorityScheduler(capacity: 32, workerCount: 2);
        var outcomes = new int[jobCount];
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int finished = 0, running = 0, exceededWorkers = 0;
        void Finish(int id)
        {
            Interlocked.Increment(ref outcomes[id]);
            if (Interlocked.Increment(ref finished) == jobCount) done.TrySetResult();
        }
        await Task.WhenAll(Enumerable.Range(0, 4).Select(producer => Task.Run(() =>
        {
            for (var id = producer; id < jobCount; id += 4)
            {
                var jobId = id;
                var handle = scheduler.Enqueue(id % 3 == 0 ? ThumbnailWorkPriority.Prefetch : ThumbnailWorkPriority.Visible,
                    async _ =>
                    {
                        if (Interlocked.Increment(ref running) > 2) Interlocked.Increment(ref exceededWorkers);
                        await Task.Yield();
                        Interlocked.Decrement(ref running);
                        Finish(jobId);
                    }, default, () => Finish(jobId));
                if (handle is null) Finish(jobId);
                else if (id % 5 == 0) scheduler.Cancel(handle.Value);
            }
        })));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, exceededWorkers);
        Assert.All(outcomes, outcome => Assert.Equal(1, outcome));
    }
}

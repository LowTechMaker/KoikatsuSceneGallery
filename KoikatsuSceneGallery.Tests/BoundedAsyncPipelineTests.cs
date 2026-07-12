using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class BoundedAsyncPipelineTests
{
    [Fact]
    public async Task ForEachAsync_NeverExceedsConfiguredConcurrency()
    {
        var active = 0;
        var peak = 0;

        await BoundedAsyncPipeline.ForEachAsync(
            Enumerable.Range(0, 40),
            3,
            async (_, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensions.Max(ref peak, current);
                try
                {
                    await Task.Delay(10, cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            CancellationToken.None);

        Assert.InRange(peak, 1, 3);
    }

    [Fact]
    public async Task ForEachAsync_CancellationStopsStartingNewWork()
    {
        using var cts = new CancellationTokenSource();
        var started = 0;
        var bothWorkersStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var processing = BoundedAsyncPipeline.ForEachAsync(
            Enumerable.Range(0, 100),
            2,
            async (_, cancellationToken) =>
            {
                if (Interlocked.Increment(ref started) == 2)
                    bothWorkersStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            cts.Token);

        await bothWorkersStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        Assert.Equal(2, Volatile.Read(ref started));
    }

    [Fact]
    public async Task ForEachAsync_DoesNotSwallowProcessorExceptions()
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BoundedAsyncPipeline.ForEachAsync(
                [1],
                1,
                (_, _) => throw new InvalidOperationException("parse failed"),
                CancellationToken.None));

        Assert.Equal("parse failed", exception.Message);
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int location, int value)
        {
            var current = Volatile.Read(ref location);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref location, value, current);
                if (observed == current)
                    return;
                current = observed;
            }
        }
    }
}

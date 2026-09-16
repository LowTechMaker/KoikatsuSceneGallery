using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class GalleryMetadataSessionTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    private sealed class Card(string path)
    {
        public string Path { get; } = path;
        public bool Loaded { get; set; }
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Operations { get; } = [];
        public void LogError(string operation, Exception exception, string? path = null) => Operations.Add(operation);
    }

    private static MetadataScanCoordinator<Card, string> CreateScan(
        Func<Card, CancellationToken, string> parse,
        Func<Action, bool> dispatch,
        RecordingLogger logger)
        => new(
            card => card.Loaded,
            TryGetCached,
            parse,
            (card, _) => card.Loaded = true,
            dispatch,
            (card, error) => logger.LogError("Scan", error, card.Path));

    private static bool TryGetCached(Card card, out string metadata)
    {
        metadata = string.Empty;
        return false;
    }

    /// <summary>
    /// The lifecycle the three galleries delegate: a stopped session refuses
    /// both entry points permanently, and the stop waits for a parse already in
    /// flight rather than abandoning it.
    /// </summary>
    [Fact]
    public async Task StoppedSessionRefusesFurtherWorkAndWaitsForParsesAlreadyRunning()
    {
        var controller = DispatcherActivation.Run(DispatcherQueueController.CreateOnDedicatedThread);
        var queue = controller.DispatcherQueue;
        var logger = new RecordingLogger();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parsed = 0;

        GalleryMetadataSession<Card, string>? session = null;
        var built = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var afterStop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new Card("first.png");
        var late = new Card("late.png");

        try
        {
            Assert.True(queue.TryEnqueue(() =>
            {
                try
                {
                    var scan = CreateScan(
                        (_, _) =>
                        {
                            Interlocked.Increment(ref parsed);
                            entered.TrySetResult();
                            release.Task.GetAwaiter().GetResult();
                            return "meta";
                        },
                        action => queue.TryEnqueue(() => action()),
                        logger);
                    session = new GalleryMetadataSession<Card, string>(
                        scan, new GalleryMetadataRefresh(queue, () => { }), logger, "TestGallery");
                    session.Start([first], () => { });
                    built.SetResult();
                }
                catch (Exception error) { built.TrySetException(error); }
            }));
            await built.Task.WaitAsync(Limit);
            await entered.Task.WaitAsync(Limit);

            Assert.True(queue.TryEnqueue(() =>
            {
                try { stopped.SetResult(session!.StopAsync()); }
                catch (Exception error) { stopped.TrySetException(error); }
            }));
            var stopTask = await stopped.Task.WaitAsync(Limit);

            // The parse is still inside the pipeline: stopping must not complete yet.
            Assert.False(stopTask.IsCompleted);
            release.SetResult();
            await stopTask.WaitAsync(Limit);

            Assert.True(queue.TryEnqueue(() =>
            {
                try
                {
                    // Permanently refused: neither entry point may start work again.
                    session!.Start([late], () => { });
                    session.Queue(late, () => { });
                    session.CancelScan();
                    session.Suspend();
                    afterStop.SetResult();
                }
                catch (Exception error) { afterStop.TrySetException(error); }
            }));
            await afterStop.Task.WaitAsync(Limit);

            // The parse that was already running ran to completion, which is what
            // the stop waited for. Its UI apply is then correctly dropped: the
            // 19th part made apply callbacks re-check the scan and worker tokens
            // when they actually execute, so a retired pass cannot write to cards.
            Assert.Equal(1, Volatile.Read(ref parsed));
            Assert.False(first.Loaded);
            Assert.False(late.Loaded);
            Assert.Empty(logger.Operations);
        }
        finally
        {
            release.TrySetResult();
            queue.TryEnqueue(() => session?.Dispose());
            await controller.ShutdownQueueAsync().AsTask().WaitAsync(Limit);
        }
    }

    /// <summary>
    /// A live session still scans, and a suspended one resumes on the next start
    /// — the difference between <see cref="GalleryMetadataSession{TCard, TMetadata}.Suspend"/>
    /// and <see cref="GalleryMetadataSession{TCard, TMetadata}.StopAsync"/>.
    /// </summary>
    [Fact]
    public async Task SuspendOnlyCancelsTheCurrentScanAndAFurtherStartResumes()
    {
        var controller = DispatcherActivation.Run(DispatcherQueueController.CreateOnDedicatedThread);
        var queue = controller.DispatcherQueue;
        var logger = new RecordingLogger();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        GalleryMetadataSession<Card, string>? session = null;
        var card = new Card("card.png");
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Assert.True(queue.TryEnqueue(() =>
            {
                try
                {
                    var scan = CreateScan((_, _) => "meta", action => queue.TryEnqueue(() => action()), logger);
                    session = new GalleryMetadataSession<Card, string>(
                        scan, new GalleryMetadataRefresh(queue, () => { }), logger, "TestGallery");
                    session.Suspend();
                    session.Start([card], () => { });
                    done.SetResult();
                }
                catch (Exception error) { done.TrySetException(error); }
            }));
            await done.Task.WaitAsync(Limit);

            // Poll the UI thread rather than sleeping: the apply is dispatched.
            while (!applied.Task.IsCompleted)
            {
                queue.TryEnqueue(() => { if (card.Loaded) applied.TrySetResult(); });
                await Task.WhenAny(applied.Task, Task.Delay(50));
            }
            await applied.Task.WaitAsync(Limit);
            Assert.Empty(logger.Operations);
        }
        finally
        {
            queue.TryEnqueue(() => session?.Dispose());
            await controller.ShutdownQueueAsync().AsTask().WaitAsync(Limit);
        }
    }
}

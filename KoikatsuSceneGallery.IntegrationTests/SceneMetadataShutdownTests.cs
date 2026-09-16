using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginCommon;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// The shutdown participant the app registers for the scene gallery: the
/// gallery's metadata session and the scene metadata cache stop together, and
/// the cache is only flushed once both have drained.
/// </summary>
/// <remarks>
/// The 22nd part covered this pairing with a bare scan coordinator. This pins
/// the shape the app actually uses now — the shared
/// <see cref="GalleryMetadataSession{TCard, TMetadata}"/> in front of the cache
/// service — because a session that refused to stop, or one that let a queued
/// card start parsing after the stop, would leave the flush incomplete.
///
/// The app's own wiring in App.xaml.cs is not reachable from a test; what is
/// verified here is that the pair behaves when composed this way.
/// </remarks>
public sealed class SceneMetadataShutdownTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    public sealed record Metadata(string Name);

    private sealed class Cache(IAppLogger logger, string path,
        Func<string, Action<Stream>, Action<Exception>, DebouncedDiskPersistence> persistence,
        Func<SceneCard, Metadata> parse)
        : MetadataCacheService<SceneCard, Metadata>(logger, path, null, persistence)
    {
        protected override Metadata Parse(SceneCard card) => parse(card);
    }

    private sealed class Harness : ControlledCachePersistence
    {
        public readonly MetadataCacheService<SceneCard, Metadata> Service;
        public Func<SceneCard, Metadata> Parser = card => new(card.FileName);
        public Harness() => Service = new Cache(this, CachePath, Create, card => Parser(card));
    }

    [Fact]
    public async Task ShutdownFlushesSceneCacheOnlyAfterTheSessionAndServiceHaveDrained()
    {
        using var h = new Harness();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanned = new SceneCard { FilePath = "in-flight.png" };
        var queued = new SceneCard { FilePath = "after-stop.png" };
        h.Parser = card =>
        {
            entered.TrySetResult();
            if (!release.Wait(Limit)) throw new TimeoutException();
            return new(card.FileName);
        };

        var scan = new MetadataScanCoordinator<SceneCard, Metadata>(
            _ => false,
            h.Service.TryGetCached,
            // Already inside a parser that ignores cancellation, as the real one is.
            (card, _) => h.Service.ParseAndCache(card),
            (_, _) => { },
            action => { action(); return true; },
            (_, error) => throw new Xunit.Sdk.XunitException(error.ToString()));
        // A real queue only because the refresh timer needs one; the callback is
        // empty, so nothing in this test depends on it ticking.
        var controller = DispatcherActivation.Run(DispatcherQueueController.CreateOnDedicatedThread);
        var session = new GalleryMetadataSession<SceneCard, Metadata>(
            scan, new GalleryMetadataRefresh(controller.DispatcherQueue, () => { }), h.Logger, "Gallery");

        session.Start([scanned], () => { });
        var shutdown = new ShutdownCoordinator(
            [new(() => Task.WhenAll(session.StopAsync(), h.Service.StopAsync()), h.Service.DisposePersistence)],
            error => throw new Xunit.Sdk.XunitException(error.ToString()));
        try
        {
            await entered.Task.WaitAsync(Limit);

            // The parse has not returned: the deadline expires without a flush,
            // and releasing persistence early is refused rather than silently
            // writing under a running parse.
            Assert.False(await shutdown.WaitAsync(TimeSpan.Zero).WaitAsync(Limit));
            Assert.Null(h.Json);
            Assert.Throws<InvalidOperationException>(h.Service.DisposePersistence);

            // A card that arrives during shutdown must not start a new parse.
            session.Queue(queued, () => { });
            session.Start([queued], () => { });
        }
        finally
        {
            release.Set();
            await shutdown.Start().WaitAsync(Limit);
            session.Dispose();
            await controller.ShutdownQueueAsync().AsTask().WaitAsync(Limit);
        }

        var written = JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!;
        Assert.Equal("in-flight.png", Assert.Single(written).Value.Name);
        Assert.False(h.Persistence!.IsDirty);
    }
}

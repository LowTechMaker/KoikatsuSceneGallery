using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginCommon;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class MetadataCachePersistenceTests
{
    [Fact]
    public void ExistingMetadataKeyCanBeReadInvalidatedAndWrittenWithoutMigration()
    {
        using var h = new Harness();
        var card = new CharacterCard { FilePath = @"C:\Cards\a.png", DateModified = new DateTime(0) };
        const string key = "DD4B79A75CCE35A5";
        File.WriteAllText(h.CachePath, "{\"DD4B79A75CCE35A5\":{\"Name\":\"existing\"}}");
        try
        {
            Assert.True(h.Service.TryGetCached(card, out var existing));
            Assert.Equal("existing", existing.Name);
            h.Service.Invalidate(card);
            h.Persistence!.Flush();
            Assert.Empty(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);
            h.Service.ParseAndCache(card);
            h.Persistence.Flush();
            Assert.Equal(key, Assert.Single(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!).Key);
        }
        finally { File.Delete(h.CachePath); }
    }

    [Fact]
    public async Task ExistingThumbnailKeyRemainsReadableWithoutRegeneration()
    {
        using var h = new Harness();
        var folder = Path.Combine(Path.GetTempPath(), "thumbnail-key-" + Guid.NewGuid().ToString("N"));
        var service = new ThumbnailCacheService(h, folder);
        var cached = Path.Combine(folder, "DD4B79A75CCE35A5.jpg");
        // This case tests the existing cache marker policy, not JPEG decoding.
        byte[] bytes = [0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9];
        try
        {
            File.WriteAllBytes(cached, bytes);
            Assert.Equal(cached, service.TryGetCachedPath(@"C:\Cards\a.png", new DateTime(0)));
            Assert.Equal(cached, await service.EnsureThumbnailAsync(@"C:\Cards\a.png", new DateTime(0))
                .WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(service.TryGetCachedPath(@"c:\cards\a.png", new DateTime(0)));
            Assert.Null(service.TryGetCachedPath(@"C:\Cards\a.png", new DateTime(1)));
            Assert.Equal(bytes, File.ReadAllBytes(cached));
        }
        finally
        {
            File.Delete(cached);
            Directory.Delete(folder);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{")]
    public void MissingNullAndHandledReadErrorsDoNotReload(string? initialJson)
    {
        using var seed = new Harness();
        using var cache = new Harness();
        using var control = new Harness();
        var card = new CharacterCard { FilePath = "read.png" };
        seed.Service.ParseAndCache(card);
        seed.Persistence.Flush();
        try
        {
            if (initialJson is not null) File.WriteAllText(cache.CachePath, initialJson);
            Assert.False(cache.Service.TryGetCached(card, out _));
            File.WriteAllBytes(cache.CachePath, seed.Json!);
            Assert.False(cache.Service.TryGetCached(card, out _));
            // Prove the same JSON/key is readable on a genuinely first load.
            File.WriteAllBytes(control.CachePath, seed.Json!);
            Assert.True(control.Service.TryGetCached(card, out var metadata));
            Assert.Equal("read.png", metadata.Name);
        }
        finally
        {
            File.Delete(cache.CachePath);
            File.Delete(control.CachePath);
        }
    }

    [Fact]
    public async Task ShutdownFlushesRealCacheOnlyAfterIgnoringCancellationWorkerReturns()
    {
        using var h = new Harness();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var card = new CharacterCard { FilePath = "late-card.png" };
        h.Parser = item =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
            return new(item.FileName);
        };
        using var scan = new MetadataScanCoordinator<CharacterCard, Metadata>(
            _ => false, h.Service.TryGetCached,
            (item, _) => h.Service.ParseAndCache(item), // Already inside a parser ignoring cancellation.
            (_, _) => { }, _ => true, (_, _) => { });
        var work = scan.Start([card], () => { }, () => { }, () => { });
        var shutdown = new ShutdownCoordinator(
            [new(() => Task.WhenAll(scan.StopAsync(default), h.Service.StopAsync()), h.Service.DisposePersistence)],
            error => throw new Xunit.Sdk.XunitException(error.ToString()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(await shutdown.WaitAsync(TimeSpan.Zero).WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Null(h.Json);
            Assert.Throws<InvalidOperationException>(h.Service.DisposePersistence);
        }
        finally
        {
            release.Set();
            try { await work.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (OperationCanceledException) { }
            await shutdown.Start().WaitAsync(TimeSpan.FromSeconds(15));
        }
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);
        Assert.False(h.Persistence!.IsDirty);
    }

    [Fact]
    public async Task StoppedServiceRejectsParsingAndIgnoresLateInvalidation()
    {
        using var h = new Harness();
        var card = new CharacterCard { FilePath = "retained.png" };
        h.Service.ParseAndCache(card);
        await h.Service.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Throws<ObjectDisposedException>(() => h.Service.ParseAndCache(card));
        h.Service.Invalidate(card);
        h.Service.DisposePersistence();
        Assert.True(h.Service.TryGetCached(card, out var metadata));
        Assert.Equal("retained.png", metadata.Name);
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);
    }

    [Fact]
    public void FailedWriteRetainsDirtyRetriesAndKeepsExistingJsonContract()
    {
        using var h = new Harness();
        var card = new CharacterCard { FilePath = "isolated-card.png", DateModified = new(2026, 1, 1) };
        h.Service.ParseAndCache(card);
        h.FailWrite = true;
        h.Persistence!.Flush();
        Assert.True(h.Persistence.IsDirty);
        Assert.Equal(TimeSpan.FromSeconds(5), h.Persistence.RetryDelay);
        Assert.Contains("MetadataCache.Flush", h.Logger.Errors);
        h.FailWrite = false;
        h.Persistence.Flush();
        Assert.False(h.Persistence.IsDirty);
        Assert.Equal(TimeSpan.Zero, h.Persistence.RetryDelay);
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);

        h.Service.Invalidate(card);
        h.Persistence.Flush();
        Assert.Empty(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);
    }

    [Fact]
    public void MutationDuringSerializationRemainsDirtyUntilNextSnapshot()
    {
        using var h = new Harness();
        var first = new CharacterCard { FilePath = "first.png" };
        var second = new CharacterCard { FilePath = "second.png" };
        h.Service.ParseAndCache(first);
        h.AfterSerialize = () => h.Service.ParseAndCache(second);
        h.Persistence!.Flush();
        Assert.True(h.Persistence.IsDirty);
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!);
        h.AfterSerialize = null;
        h.Persistence.Flush();
        Assert.False(h.Persistence.IsDirty);
        Assert.Equal(2, JsonSerializer.Deserialize<Dictionary<string, Metadata>>(h.Json!)!.Count);
    }

    public sealed record Metadata(string Name);
    private sealed class Cache(IAppLogger logger, string path,
        Func<string, Action<Stream>, Action<Exception>, DebouncedDiskPersistence> persistence,
        Func<CharacterCard, Metadata> parse)
        : MetadataCacheService<CharacterCard, Metadata>(logger, path, null, persistence)
    {
        protected override Metadata Parse(CharacterCard card) => parse(card);
    }
    private sealed class Harness : ControlledCachePersistence
    {
        public readonly MetadataCacheService<CharacterCard, Metadata> Service;
        public Func<CharacterCard, Metadata> Parser = card => new(card.FileName);
        public Harness() => Service = new Cache(this, CachePath, Create, card => Parser(card));
    }
}

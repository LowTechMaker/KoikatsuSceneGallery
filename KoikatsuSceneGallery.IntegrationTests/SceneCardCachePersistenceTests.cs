using System.Text.Json;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class SceneCardCachePersistenceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("null", true)]
    [InlineData("{", false)]
    public void MissingAndNullFilesRetryButHandledReadErrorsRemainFinal(string? initialJson, bool reloads)
    {
        using var writer = new ControlledCachePersistence();
        using var cache = new SceneCardCacheService(writer, writer.CachePath, writer.Create);
        var entry = new CachedCardEntry("read.png", 1, 2, 3, 4, null);
        try
        {
            if (initialJson is not null) File.WriteAllText(writer.CachePath, initialJson);
            Assert.Empty(cache.LoadAll());
            File.WriteAllText(writer.CachePath, JsonSerializer.Serialize(
                new Dictionary<string, CachedCardEntry> { [entry.FilePath] = entry }));
            if (reloads) Assert.Equal(entry, Assert.Single(cache.LoadAll()).Value);
            else Assert.Empty(cache.LoadAll());
        }
        finally { File.Delete(writer.CachePath); }
    }

    [Fact]
    public async Task ShutdownRejectsLateScanWatcherAndThumbnailWrites()
    {
        using var writer = new ControlledCachePersistence();
        using var cache = new SceneCardCacheService(writer, writer.CachePath, writer.Create);
        var original = new CachedCardEntry("original.png", 1, 2, 3, 4, "original.jpg");
        cache.Add(original);
        await cache.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
        cache.UpdateAll(new Dictionary<string, CachedCardEntry>());
        cache.Add(new("late.png", 5, 6, 7, 8, null));
        cache.SetThumbnailPath(original.FilePath, "late.jpg");
        cache.Remove(original.FilePath);
        cache.Dispose();
        Assert.Equal(original, Assert.Single(cache.LoadAll()).Value);
        Assert.Equal(original, Assert.Single(
            JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!).Value);
        Assert.False(writer.Persistence.IsDirty);
    }

    [Fact]
    public void SharedPersistencePreservesCaseInsensitiveMutationsAndEntrySchema()
    {
        using var writer = new ControlledCachePersistence();
        using var cache = new SceneCardCacheService(writer, writer.CachePath, writer.Create);
        var entry = new CachedCardEntry("Card.PNG", 10, 100, 200, 300, null);
        cache.Add(entry);
        cache.SetThumbnailPath("card.png", "thumb.jpg");
        Assert.Equal("thumb.jpg", cache.LoadAll()["CARD.PNG"].ThumbnailPath);
        writer.Persistence.Flush();
        var saved = JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!;
        Assert.Equal(entry with { ThumbnailPath = "thumb.jpg" }, Assert.Single(saved).Value);

        cache.Remove("cArD.PnG");
        writer.Persistence.Flush();
        Assert.Empty(JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!);
        cache.UpdateAll(new Dictionary<string, CachedCardEntry> { [entry.FilePath] = entry });
        cache.Dispose();
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!);
        Assert.False(writer.Persistence.IsDirty);
    }

    [Fact]
    public void FailedWriteRetriesAndMutationDuringSnapshotRequiresAnotherSave()
    {
        using var writer = new ControlledCachePersistence();
        using var cache = new SceneCardCacheService(writer, writer.CachePath, writer.Create);
        cache.Add(new("first.png", 1, 1, 1, 1, null));
        writer.FailWrite = true;
        writer.Persistence.Flush();
        Assert.True(writer.Persistence.IsDirty);
        Assert.Equal(TimeSpan.FromSeconds(5), writer.Persistence.RetryDelay);
        Assert.Contains("SceneCardCache.Flush", writer.Errors);
        writer.FailWrite = false;
        writer.AfterSerialize = () => cache.Add(new("second.png", 2, 2, 2, 2, null));
        writer.Persistence.Flush();
        Assert.True(writer.Persistence.IsDirty);
        Assert.Single(JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!);
        writer.AfterSerialize = null;
        writer.Persistence.Flush();
        Assert.False(writer.Persistence.IsDirty);
        Assert.Equal(TimeSpan.Zero, writer.Persistence.RetryDelay);
        Assert.Equal(2, JsonSerializer.Deserialize<Dictionary<string, CachedCardEntry>>(writer.Json!)!.Count);
    }
}

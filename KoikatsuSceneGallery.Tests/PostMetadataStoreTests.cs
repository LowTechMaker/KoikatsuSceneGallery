using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class PostMetadataStoreTests
{
    private static readonly DateTimeOffset FetchedAt =
        new(2026, 9, 2, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task WriteAndRead_RoundTripsVersionedMetadataInHiddenSidecar()
    {
        using var directory = new TestDirectory();
        var authorDirectory = Path.Combine(directory.Path, "作者");
        Directory.CreateDirectory(authorDirectory);
        var store = new PostMetadataStore();
        var document = CreateDocument("作品標題");

        Assert.True(await store.WriteAsync(authorDirectory, document));

        var path = store.GetSidecarPath(authorDirectory, "pixiv", "12345");
        Assert.Equal(
            Path.Combine(authorDirectory, ".scenegallery", "fetched_data", "pixiv_12345.json"),
            path);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        var loaded = Assert.IsType<PostMetadataDocument>(store.Read(authorDirectory, "pixiv", "12345"));
        Assert.Equal(document.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(document.ProviderId, loaded.ProviderId);
        Assert.Equal(document.ArtworkId, loaded.ArtworkId);
        Assert.Equal(document.AuthorName, loaded.AuthorName);
        Assert.Equal(document.AuthorId, loaded.AuthorId);
        Assert.Equal(document.Title, loaded.Title);
        Assert.Equal(document.Description, loaded.Description);
        Assert.Equal(document.Rating, loaded.Rating);
        Assert.Equal(document.FetchedAt, loaded.FetchedAt);
        Assert.Equal(document.Tags, loaded.Tags);
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());

        if (OperatingSystem.IsWindows())
        {
            var metadataDirectory = Path.Combine(authorDirectory, ".scenegallery");
            Assert.True(File.GetAttributes(metadataDirectory).HasFlag(FileAttributes.Hidden));
        }
    }

    [Fact]
    public async Task Write_DoesNotReplaceNewerMetadataWithOlderFetch()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var newer = CreateDocument("newer") with { FetchedAt = FetchedAt.AddMinutes(1) };
        var older = CreateDocument("older");

        Assert.True(await store.WriteAsync(directory.Path, newer));
        Assert.False(await store.WriteAsync(directory.Path, older));
        Assert.Equal("newer", store.Read(directory.Path, "pixiv", "12345")?.Title);
    }

    [Fact]
    public async Task ReadAll_IgnoresUnsupportedSchema()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var document = CreateDocument("title");
        await store.WriteAsync(directory.Path, document);

        var path = store.GetSidecarPath(directory.Path, document.ProviderId, document.ArtworkId);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(document with
            {
                SchemaVersion = PostMetadataDocument.CurrentSchemaVersion + 1,
            }));

        Assert.Empty(store.ReadAll(directory.Path));
    }

    private static PostMetadataDocument CreateDocument(string title) => new(
        PostMetadataDocument.CurrentSchemaVersion,
        "pixiv",
        "12345",
        "作者",
        "987",
        title,
        "說明",
        1,
        [new PostMetadataTag("制服", "uniform")],
        FetchedAt);
}

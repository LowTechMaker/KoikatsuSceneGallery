using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class PostMetadataStoreTests
{
    private static readonly DateTimeOffset FetchedAt =
        new(2026, 7, 28, 12, 34, 56, TimeSpan.Zero);

    [Fact]
    public async Task WriteAndRead_RoundTripsVersionedNormalizedJson()
    {
        using var directory = new TestDirectory();
        var authorDirectory = Path.Combine(directory.Path, "作者");
        Directory.CreateDirectory(authorDirectory);
        var store = new PostMetadataStore();
        var document = CreateDocument(
            title: "作品標題",
            description: "貼文說明",
            tags: [new("制服", "uniform"), new("場景", null)]);

        Assert.True(await store.WriteAsync(authorDirectory, document));

        var path = store.GetSidecarPath(authorDirectory, "pixiv", "12345");
        Assert.Equal(
            Path.Combine(
                authorDirectory,
                ".scenegallery",
                "fetched_data",
                "pixiv_12345.json"),
            path);
        Assert.True(File.Exists(path));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        var loaded = Assert.IsType<PostMetadataDocument>(
            store.Read(authorDirectory, "pixiv", "12345"));
        Assert.Equal(PostMetadataDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(document.ProviderId, loaded.ProviderId);
        Assert.Equal(document.ArtworkId, loaded.ArtworkId);
        Assert.Equal(document.AuthorName, loaded.AuthorName);
        Assert.Equal(document.AuthorId, loaded.AuthorId);
        Assert.Equal(document.Title, loaded.Title);
        Assert.Equal(document.Description, loaded.Description);
        Assert.Equal(document.Rating, loaded.Rating);
        Assert.Equal(document.FetchedAt, loaded.FetchedAt);
        Assert.Collection(
            loaded.Tags,
            tag =>
            {
                Assert.Equal("制服", tag.Name);
                Assert.Equal("uniform", tag.TranslatedName);
            },
            tag =>
            {
                Assert.Equal("場景", tag.Name);
                Assert.Null(tag.TranslatedName);
            });

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("pixiv", json.RootElement.GetProperty("providerId").GetString());

        if (OperatingSystem.IsWindows())
        {
            var metadataDirectory = Path.Combine(authorDirectory, ".scenegallery");
            Assert.True(
                File.GetAttributes(metadataDirectory).HasFlag(FileAttributes.Hidden));
        }
    }

    [Fact]
    public async Task Read_RejectsIdentityMismatchAndUnsupportedSchema()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var document = CreateDocument();
        await store.WriteAsync(directory.Path, document);

        Assert.Null(store.Read(directory.Path, "bepisdb", document.ArtworkId));
        Assert.Null(store.Read(directory.Path, document.ProviderId, "different"));

        var path = store.GetSidecarPath(
            directory.Path,
            document.ProviderId,
            document.ArtworkId);
        var unsupported = document with
        {
            SchemaVersion = PostMetadataDocument.CurrentSchemaVersion + 1,
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(unsupported));

        Assert.Null(store.Read(
            directory.Path,
            document.ProviderId,
            document.ArtworkId));
        Assert.Empty(store.ReadAll(directory.Path));
    }

    [Fact]
    public async Task Write_OlderFetchCannotOverwriteNewerMetadata()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var newer = CreateDocument(title: "newer") with
        {
            FetchedAt = FetchedAt.AddMinutes(1),
        };
        var older = CreateDocument(title: "older");
        var newest = CreateDocument(title: "newest") with
        {
            FetchedAt = FetchedAt.AddMinutes(2),
        };

        Assert.True(await store.WriteAsync(directory.Path, newer));
        Assert.False(await store.WriteAsync(directory.Path, older));
        Assert.Equal(
            "newer",
            store.Read(directory.Path, "pixiv", "12345")?.Title);

        Assert.True(await store.WriteAsync(directory.Path, newest));
        Assert.Equal(
            "newest",
            store.Read(directory.Path, "pixiv", "12345")?.Title);
    }

    [Fact]
    public async Task Path_EscapesProviderAndArtworkSeparatorsWithoutAddingDirectories()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var document = CreateDocument() with
        {
            ProviderId = "fanbox/web",
            ArtworkId = "creator:post/42",
        };

        await store.WriteAsync(directory.Path, document);

        var path = store.GetSidecarPath(
            directory.Path,
            document.ProviderId,
            document.ArtworkId);
        Assert.Equal(
            "fanbox%2Fweb_creator%3Apost%2F42.json",
            Path.GetFileName(path));
        Assert.Single(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.json"));
    }

    [Fact]
    public async Task Delete_RemovesOnlyTheRequestedSidecar()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var first = CreateDocument();
        var second = CreateDocument() with { ArtworkId = "67890" };
        await store.WriteAsync(directory.Path, first);
        await store.WriteAsync(directory.Path, second);

        Assert.True(store.Delete(directory.Path, first.ProviderId, first.ArtworkId));
        Assert.False(store.Delete(directory.Path, first.ProviderId, first.ArtworkId));
        Assert.Null(store.Read(directory.Path, first.ProviderId, first.ArtworkId));
        Assert.NotNull(store.Read(directory.Path, second.ProviderId, second.ArtworkId));
    }

    [Fact]
    public async Task Write_RejectsInvalidProviderOrArtworkIdentity()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(
                directory.Path,
                CreateDocument() with { ProviderId = " " }));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(
                directory.Path,
                CreateDocument() with { ArtworkId = "" }));
    }

    private static PostMetadataDocument CreateDocument(
        string? title = "title",
        string? description = null,
        IReadOnlyList<PostMetadataTag>? tags = null)
        => new(
            PostMetadataDocument.CurrentSchemaVersion,
            "pixiv",
            "12345",
            "作者",
            "987",
            title,
            description,
            1,
            tags ?? [],
            FetchedAt);
}

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
        Assert.Equal(PostMetadataDocument.CurrentSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetInt32());
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
    public async Task WriteAndRead_RoundTripsLocalFileNames()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var document = CreateDocument() with
        {
            LocalFileNames = ["scene_001.png", "scene_002.png"],
        };

        Assert.True(await store.WriteAsync(directory.Path, document));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        Assert.Equal(["scene_001.png", "scene_002.png"], loaded!.LocalFileNames);
    }

    [Fact]
    public async Task Read_LegacySidecarWithoutLocalFileNamesReturnsEmpty()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();

        // Simulate a v1 sidecar by writing JSON that omits localFileNames.
        var legacyJson = $$"""
        {
          "schemaVersion": 1,
          "providerId": "pixiv",
          "artworkId": "12345",
          "authorName": "作者",
          "authorId": "987",
          "title": "title",
          "description": null,
          "rating": 1,
          "tags": [],
          "fetchedAt": "2026-07-28T12:34:56+00:00"
        }
        """;
        var path = store.GetSidecarPath(directory.Path, "pixiv", "12345");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, legacyJson);

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        Assert.Empty(loaded!.LocalFileNames);
        Assert.Equal("title", loaded.Title);
    }

    [Fact]
    public async Task Write_MergingIntoALegacySidecarUpgradesItsSchemaVersion()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();

        // A v1 sidecar predates LocalFileNames entirely.
        var legacyJson = """
        {
          "schemaVersion": 1,
          "providerId": "pixiv",
          "artworkId": "12345",
          "authorName": "作者",
          "authorId": "987",
          "title": "legacy title",
          "description": "legacy description",
          "rating": 2,
          "tags": [{ "name": "制服", "translatedName": "uniform" }],
          "fetchedAt": "2026-07-28T12:34:56+00:00"
        }
        """;
        var path = store.GetSidecarPath(directory.Path, "pixiv", "12345");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, legacyJson);

        Assert.True(await store.WriteAsync(
            directory.Path,
            CreateDocument() with { LocalFileNames = ["page1.png"] }));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        // The file now carries current-schema fields, so it says so.
        Assert.Equal(PostMetadataDocument.CurrentSchemaVersion, loaded!.SchemaVersion);
        Assert.Equal(["page1.png"], loaded.LocalFileNames);
        // The stored metadata is the fresher one and survives untouched.
        Assert.Equal("legacy title", loaded.Title);
        Assert.Equal("legacy description", loaded.Description);
        Assert.Equal(2, loaded.Rating);
        Assert.Equal(FetchedAt, loaded.FetchedAt);
        var tag = Assert.Single(loaded.Tags);
        Assert.Equal("制服", tag.Name);
        Assert.Equal("uniform", tag.TranslatedName);
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
    public async Task Write_MergesLocalFileNamesWhenFetchedAtIsUnchanged()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var firstPage = CreateDocument(title: "stored") with
        {
            LocalFileNames = ["page1.png"],
        };
        // A second page imported from the same cached artwork info carries the
        // very same FetchedAt.
        var secondPage = CreateDocument(title: "stored") with
        {
            LocalFileNames = ["page2.png"],
        };

        Assert.True(await store.WriteAsync(directory.Path, firstPage));
        Assert.True(await store.WriteAsync(directory.Path, secondPage));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        Assert.Equal(["page1.png", "page2.png"], loaded!.LocalFileNames);
        Assert.Equal(FetchedAt, loaded.FetchedAt);
    }

    [Fact]
    public async Task Write_KeepsExistingFileNamesWhenOlderMetadataIsRejected()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var newer = CreateDocument(title: "newer") with
        {
            FetchedAt = FetchedAt.AddMinutes(1),
            LocalFileNames = ["page1.png"],
        };
        var older = CreateDocument(title: "older") with
        {
            LocalFileNames = ["page2.png"],
        };

        Assert.True(await store.WriteAsync(directory.Path, newer));
        Assert.True(await store.WriteAsync(directory.Path, older));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        // The stale metadata must not win, but its file name is still recorded.
        Assert.Equal("newer", loaded!.Title);
        Assert.Equal(newer.FetchedAt, loaded.FetchedAt);
        Assert.Equal(["page1.png", "page2.png"], loaded.LocalFileNames);
    }

    [Fact]
    public async Task Write_MergesFileNamesIntoFreshlyFetchedMetadata()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var stored = CreateDocument(title: "stored") with
        {
            LocalFileNames = ["page1.png"],
        };
        var refetched = CreateDocument(title: "refetched") with
        {
            FetchedAt = FetchedAt.AddMinutes(1),
            LocalFileNames = ["page2.png"],
        };

        Assert.True(await store.WriteAsync(directory.Path, stored));
        Assert.True(await store.WriteAsync(directory.Path, refetched));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        Assert.Equal("refetched", loaded!.Title);
        Assert.Equal(["page1.png", "page2.png"], loaded.LocalFileNames);
    }

    [Fact]
    public async Task Write_SkipsRewriteWhenNothingChanged()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var document = CreateDocument() with
        {
            LocalFileNames = ["page1.png"],
        };

        Assert.True(await store.WriteAsync(directory.Path, document));
        Assert.False(await store.WriteAsync(directory.Path, document));
        // The same names in a different casing are the same files on Windows.
        Assert.False(await store.WriteAsync(
            directory.Path,
            document with { LocalFileNames = ["PAGE1.PNG"] }));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.Equal(["page1.png"], loaded!.LocalFileNames);
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

    [Fact]
    public async Task Write_SerializesConcurrentWritersAndReleasesTheLock()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        const int writerCount = 32;

        // Every writer merges its own file name into the sidecar. A name can
        // only go missing if two writers read-modify-write at the same time,
        // so the assertion below is a mutual-exclusion check.
        await Task.WhenAll(Enumerable.Range(0, writerCount).Select(index =>
            Task.Run(() => store.WriteAsync(
                directory.Path,
                CreateDocument() with { LocalFileNames = [$"page{index}.png"] }))));

        var loaded = store.Read(directory.Path, "pixiv", "12345");
        Assert.NotNull(loaded);
        Assert.Equal(
            Enumerable.Range(0, writerCount).Select(index => $"page{index}.png").Order(),
            loaded!.LocalFileNames.Order());
        Assert.Equal(0, PostMetadataStore.ActiveWriteLockCount);
    }

    [Fact]
    public async Task Write_DoesNotAccumulateLocksAcrossSidecars()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();

        for (var index = 0; index < 16; index++)
        {
            await store.WriteAsync(
                directory.Path,
                CreateDocument() with { ArtworkId = index.ToString() });
        }

        Assert.Equal(0, PostMetadataStore.ActiveWriteLockCount);
    }

    [Fact]
    public async Task Write_ReleasesTheLockWhenCancelledOrRejected()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.WriteAsync(
                directory.Path,
                CreateDocument(),
                cancellation.Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => store.WriteAsync(
                directory.Path,
                CreateDocument() with { ProviderId = " " }));

        Assert.Equal(0, PostMetadataStore.ActiveWriteLockCount);
    }

    [Fact]
    public async Task Write_CancellingAWaiterLeavesTheHolderAndTheEntryIntact()
    {
        using var directory = new TestDirectory();
        var store = new PostMetadataStore();
        var path = store.GetSidecarPath(directory.Path, "pixiv", "12345");

        // Writer A holds the per-path lock.
        using var holder = await PostMetadataStore.HoldWriteLockAsync(path);
        Assert.Equal(1, PostMetadataStore.GetWriteLockReferenceCount(path));

        // Writer B takes a reference and blocks in WaitAsync behind A.
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = store.WriteAsync(
            directory.Path,
            CreateDocument(title: "blocked"),
            waiterCancellation.Token);
        await WaitForReferenceCountAsync(path, 2);
        Assert.False(waiter.IsCompleted);

        await waiterCancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);

        // B dropped only its own reference; A still owns the entry.
        Assert.Equal(1, PostMetadataStore.GetWriteLockReferenceCount(path));
        Assert.Equal(1, PostMetadataStore.ActiveWriteLockCount);

        holder.Dispose();
        Assert.Equal(0, PostMetadataStore.ActiveWriteLockCount);

        // The entry was disposed only once nobody could reach it, so a later
        // writer gets a fresh lock and writes normally.
        Assert.True(await store.WriteAsync(
            directory.Path,
            CreateDocument(title: "after")));
        Assert.Equal("after", store.Read(directory.Path, "pixiv", "12345")?.Title);
        Assert.Equal(0, PostMetadataStore.ActiveWriteLockCount);
    }

    private static async Task WaitForReferenceCountAsync(string path, int expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (PostMetadataStore.GetWriteLockReferenceCount(path) != expected)
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                $"The write lock reference count never reached {expected}.");
            await Task.Delay(10);
        }
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

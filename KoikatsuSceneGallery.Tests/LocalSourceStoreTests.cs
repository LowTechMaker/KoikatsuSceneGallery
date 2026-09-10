using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class LocalSourceStoreTests
{
    [Fact]
    public async Task WriteAndRead_RoundTripsInTheHiddenMetadataDirectory()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");
        var store = new LocalSourceStore();
        var document = CreateDocument() with
        {
            Aliases = ["A明"],
            Note = "Discord 私傳",
        };

        await store.WriteAsync(sourceDirectory, document);

        var path = store.GetDocumentPath(sourceDirectory);
        Assert.Equal(Path.Combine(sourceDirectory, ".scenegallery", "source.json"), path);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.True(File.GetAttributes(Path.GetDirectoryName(path)!).HasFlag(FileAttributes.Hidden));

        var loaded = Assert.IsType<LocalSourceDocument>(store.Read(sourceDirectory));
        Assert.Equal(document.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal(document.DisplayName, loaded.DisplayName);
        Assert.Equal(document.Aliases, loaded.Aliases);
        Assert.Equal(document.Note, loaded.Note);
        Assert.Equal(document.CreatedAt, loaded.CreatedAt);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(json.RootElement.TryGetProperty("displayName", out _));
    }

    // Renaming a source must not move cards, so the identity and creation time
    // survive while only the editable parts change.
    [Fact]
    public async Task UpdateAsync_RenamesWithoutLosingIdentityOrCreationTime()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");
        var store = new LocalSourceStore();
        var original = await store.UpdateAsync(sourceDirectory, "local-k7f3q9", "阿明", ["A明"], "私傳");

        var renamed = await store.UpdateAsync(sourceDirectory, "local-k7f3q9", "小明");

        Assert.Equal("local-k7f3q9", renamed.Id);
        Assert.Equal("小明", renamed.DisplayName);
        Assert.Equal(original.CreatedAt, renamed.CreatedAt);
        Assert.Equal(["A明"], renamed.Aliases);
        Assert.Equal("私傳", renamed.Note);
        Assert.True(renamed.UpdatedAt >= original.UpdatedAt);
        Assert.Equal("小明", store.Read(sourceDirectory)?.DisplayName);
    }

    [Fact]
    public void Read_ReturnsNullWhenTheSourceHasNoDocument()
    {
        using var directory = new TestDirectory();

        Assert.Null(new LocalSourceStore().Read(CreateSourceDirectory(directory, "阿明 (local-k7f3q9)")));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"local-k7f3q9\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"nope\", \"displayName\": \"阿明\" }")]
    [InlineData("{ \"schemaVersion\": 99, \"id\": \"local-k7f3q9\", \"displayName\": \"阿明\" }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"local-k7f3q9\", \"displayName\": \"阿明\", \"aliases\": [\" \"] }")]
    public async Task Read_TreatsADamagedDocumentAsAbsentRatherThanThrowing(string contents)
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");
        var store = new LocalSourceStore();
        var path = store.GetDocumentPath(sourceDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);

        Assert.Null(store.Read(sourceDirectory));
    }

    [Fact]
    public async Task WriteAsync_RejectsAnInvalidIdentity()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");

        await Assert.ThrowsAsync<ArgumentException>(() => new LocalSourceStore()
            .WriteAsync(sourceDirectory, CreateDocument() with { Id = "12345678" }));
    }

    [Fact]
    public async Task GetAvatarPath_ResolvesToAnAbsolutePathOnlyWhenTheFileExists()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");
        var store = new LocalSourceStore();
        var document = CreateDocument() with { AvatarFileName = "avatar.png" };
        await store.WriteAsync(sourceDirectory, document);

        Assert.Null(store.GetAvatarPath(sourceDirectory, document));

        var avatarPath = Path.Combine(sourceDirectory, ".scenegallery", "avatar.png");
        await File.WriteAllBytesAsync(avatarPath, [1, 2, 3]);

        var resolved = store.GetAvatarPath(sourceDirectory, document);
        Assert.Equal(avatarPath, resolved);
        Assert.True(Path.IsPathFullyQualified(resolved!));
        Assert.Null(store.GetAvatarPath(sourceDirectory, CreateDocument()));
    }

    // The stored value is a file name so the library stays portable; a path
    // that tries to escape the metadata directory must not be honoured.
    [Fact]
    public async Task GetAvatarPath_IgnoresDirectoryComponentsInTheStoredName()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = CreateSourceDirectory(directory, "阿明 (local-k7f3q9)");
        var store = new LocalSourceStore();
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "outside.png"), [1]);

        var escaping = CreateDocument() with { AvatarFileName = Path.Combine("..", "..", "outside.png") };

        Assert.Null(store.GetAvatarPath(sourceDirectory, escaping));
    }

    private static string CreateSourceDirectory(TestDirectory directory, string folderName)
    {
        var path = Path.Combine(directory.Path, folderName);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LocalSourceDocument CreateDocument()
        => new(LocalSourceDocument.CurrentSchemaVersion, "local-k7f3q9", "阿明")
        {
            CreatedAt = new DateTimeOffset(2026, 9, 2, 12, 34, 56, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 9, 2, 12, 34, 56, TimeSpan.Zero),
        };
}

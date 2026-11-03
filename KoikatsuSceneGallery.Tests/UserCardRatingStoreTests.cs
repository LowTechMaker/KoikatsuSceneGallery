using System.Text.Json;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class UserCardRatingStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static UserCardRatingStore Store() => new(() => Now);

    [Fact]
    public async Task ARatingIsStoredInTheRootsHiddenFolderAndSurvivesAReload()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "R-18", "author", "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.True(await store.SetAsync(card, "hash-a", 2));

        var file = UserCardRatingStore.GetFilePath(directory.Path);
        Assert.Equal(Path.Combine(directory.Path, ".scenegallery", "user_ratings.json"), file);
        using (var json = JsonDocument.Parse(File.ReadAllText(file)))
        {
            var entry = json.RootElement.GetProperty("ratings")[0];
            Assert.Equal("hash-a", entry.GetProperty("hash").GetString());
            Assert.Equal(2, entry.GetProperty("rating").GetInt32());
            Assert.Equal(Path.Combine("R-18", "author", "a.png"), entry.GetProperty("relativePath").GetString());
        }

        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        Assert.Equal(2, reloaded.TryGetByPath(card));
        Assert.Equal(2, reloaded.TryGetByHash("hash-a"));
        Assert.Null(reloaded.TryGetByPath(Path.Combine(directory.Path, "other.png")));
    }

    [Fact]
    public async Task AMovedCardIsFoundByItsHashAndItsPathIsUpdated()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "old", "a.png");
        var newPath = Path.Combine(directory.Path, "new", "renamed.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetAsync(oldPath, "hash-a", 1);

        Assert.Null(store.TryGetByPath(newPath));
        Assert.Equal(1, await store.ResolveAsync(newPath, "hash-a"));

        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        Assert.Equal(1, reloaded.TryGetByPath(newPath));
        Assert.Null(reloaded.TryGetByPath(oldPath));
    }

    [Fact]
    public async Task ACardMovedToAnotherRootLeavesTheOldRootsFile()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first");
        var second = Path.Combine(directory.Path, "second");
        var store = Store();
        store.EnsureLoaded([first, second]);
        await store.SetAsync(Path.Combine(first, "a.png"), "hash-a", 0);

        await store.SetAsync(Path.Combine(second, "a.png"), "hash-a", 1);

        Assert.DoesNotContain("hash-a", File.ReadAllText(UserCardRatingStore.GetFilePath(first)));
        Assert.Contains("hash-a", File.ReadAllText(UserCardRatingStore.GetFilePath(second)));
        Assert.Equal(1, store.TryGetByPath(Path.Combine(second, "a.png")));
    }

    [Fact]
    public async Task ClearingForgetsTheRating()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetAsync(card, "hash-a", 1);
        string? changed = null;
        store.RatingChanged += path => changed = path;

        Assert.True(await store.ClearAsync(card, "hash-a"));

        Assert.Null(store.TryGetByPath(card));
        Assert.Equal(card, changed);
        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        Assert.Equal(0, reloaded.Count);
    }

    [Fact]
    public async Task ACardOutsideEveryRootCannotBeRated()
    {
        using var directory = new TestDirectory();
        var store = Store();
        store.EnsureLoaded([Path.Combine(directory.Path, "library")]);

        Assert.False(await store.SetAsync(Path.Combine(directory.Path, "elsewhere", "a.png"), "hash-a", 1));
        Assert.False(await store.SetAsync(Path.Combine(directory.Path, "library-2", "a.png"), "hash-a", 1));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public async Task ADamagedFileIsMovedAsideInsteadOfOverwritten()
    {
        using var directory = new TestDirectory();
        var file = UserCardRatingStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ not json");
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.True(await store.SetAsync(Path.Combine(directory.Path, "a.png"), "hash-a", 1));

        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "user_ratings.json.unreadable-*"));
        Assert.Equal("{ not json", File.ReadAllText(aside));
    }

    [Fact]
    public async Task ALockedFileMakesItsRootReadOnly()
    {
        using var directory = new TestDirectory();
        var file = UserCardRatingStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{}");
        var store = Store();
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.EnsureLoaded([directory.Path]);

        Assert.False(await store.SetAsync(Path.Combine(directory.Path, "a.png"), "hash-a", 1));
        Assert.Equal("{}", File.ReadAllText(file));
    }

    [Fact]
    public async Task SetRejectsAnUnknownRating()
    {
        using var directory = new TestDirectory();
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetAsync(Path.Combine(directory.Path, "a.png"), "hash-a", 3));
    }
}

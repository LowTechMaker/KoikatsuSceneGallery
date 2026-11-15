using System.Text.Json;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardStateStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    private static CardStateStore Store() => new(() => Now);

    [Fact]
    public async Task AStateIsStoredInTheRootsHiddenFolderAndSurvivesANewInstance()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "scenes", "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.True(await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true));

        var file = CardStateStore.GetFilePath(directory.Path);
        Assert.Equal(Path.Combine(directory.Path, ".scenegallery", "card_states.json"), file);
        using (var json = JsonDocument.Parse(File.ReadAllText(file)))
        {
            Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            var entry = json.RootElement.GetProperty("states")[0];
            Assert.Equal("hash-a", entry.GetProperty("hash").GetString());
            Assert.True(entry.GetProperty("liked").GetBoolean());
            Assert.Equal(Path.Combine("scenes", "a.png"), entry.GetProperty("relativePath").GetString());
        }

        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        var state = reloaded.TryGetByPath(card);
        Assert.NotNull(state);
        Assert.True(state.Liked);
        Assert.False(state.Queued);
        Assert.Equal(Now, state.UpdatedAt);
        Assert.Null(reloaded.TryGetByPath(Path.Combine(directory.Path, "other.png")));
    }

    [Fact]
    public async Task OnlyStoredFieldsAreWrittenNotComputedOnes()
    {
        using var directory = new TestDirectory();
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(Path.Combine(directory.Path, "a.png"), "hash-a", CardFlag.Queued, true);
        await store.MarkUsedAsync(Path.Combine(directory.Path, "b.png"), "hash-b");

        using var json = JsonDocument.Parse(File.ReadAllText(CardStateStore.GetFilePath(directory.Path)));
        foreach (var entry in json.RootElement.GetProperty("states").EnumerateArray())
        {
            // Exactly the record's positional fields: IsEmpty is derived from
            // them and would only go stale or be trusted by another reader.
            Assert.Equal(
                new[] { "hash", "relativePath", "queued", "liked", "hidden", "lastUsedAt", "useCount", "updatedAt" }
                    .Order(StringComparer.Ordinal),
                entry.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
            Assert.False(entry.TryGetProperty("isEmpty", out _));
        }
    }

    [Fact]
    public void AFileWrittenWithIsEmptyStillLoads()
    {
        // Wave 1 wrote the computed property; those files must keep working.
        using var directory = new TestDirectory();
        var file = CardStateStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """
            { "schemaVersion": 1, "states": [ { "hash": "hash-a", "relativePath": "a.png", "queued": true,
              "liked": false, "hidden": false, "lastUsedAt": null, "useCount": 0,
              "updatedAt": "2026-11-15T12:00:00+00:00", "isEmpty": false } ] }
            """);
        var store = Store();

        store.EnsureLoaded([directory.Path]);

        Assert.True(store.TryGetByPath(Path.Combine(directory.Path, "a.png"))?.Queued);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.unreadable-*"));
    }

    [Fact]
    public async Task EnsureLoadedAddsRootsInsteadOfReplacingThem()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "scenes");
        var second = Path.Combine(directory.Path, "characters");
        var writer = Store();
        writer.EnsureLoaded([first, second]);
        await writer.SetFlagAsync(Path.Combine(first, "a.png"), "hash-a", CardFlag.Liked, true);
        await writer.SetFlagAsync(Path.Combine(second, "b.png"), "hash-b", CardFlag.Queued, true);

        var store = Store();
        store.EnsureLoaded([first]);
        store.EnsureLoaded([second]);

        Assert.NotNull(store.TryGetByPath(Path.Combine(first, "a.png")));
        Assert.NotNull(store.TryGetByPath(Path.Combine(second, "b.png")));
        Assert.True(await store.SetFlagAsync(Path.Combine(first, "c.png"), "hash-c", CardFlag.Hidden, true));
        Assert.Equal(3, store.Snapshot().Count);
    }

    [Fact]
    public async Task EnsureLoadedDoesNotRereadARootAlreadyLoaded()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);

        // Another writer changes the file; an additive load must keep what is
        // in memory rather than re-reading the root behind the store's back.
        File.Delete(CardStateStore.GetFilePath(directory.Path));
        store.EnsureLoaded([directory.Path]);

        Assert.True(store.TryGetByPath(card)?.Liked);
    }

    [Fact]
    public async Task HidingClearsQueued()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, true);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);

        await store.SetFlagAsync(card, "hash-a", CardFlag.Hidden, true);

        var state = store.TryGetByPath(card)!;
        Assert.True(state.Hidden);
        Assert.False(state.Queued);
        Assert.True(state.Liked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task QueueingOrLikingClearsHidden(bool queue)
    {
        var flag = queue ? CardFlag.Queued : CardFlag.Liked;
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Hidden, true);

        await store.SetFlagAsync(card, "hash-a", flag, true);

        var state = store.TryGetByPath(card)!;
        Assert.False(state.Hidden);
        Assert.True(state.Has(flag));
    }

    [Fact]
    public async Task ClearingAFlagDoesNotTouchTheOthers()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Hidden, true);

        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, false);

        Assert.True(store.TryGetByPath(card)?.Hidden);
    }

    [Fact]
    public async Task MarkUsedRecordsTheUseAndClearsQueued()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var clock = Now;
        var store = new CardStateStore(() => clock);
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, true);

        clock = Now.AddHours(1);
        Assert.True(await store.MarkUsedAsync(card, "hash-a"));
        clock = Now.AddHours(2);
        Assert.True(await store.MarkUsedAsync(card, "hash-a"));

        var state = store.TryGetByPath(card)!;
        Assert.False(state.Queued);
        Assert.Equal(Now.AddHours(2), state.LastUsedAt);
        Assert.Equal(2, state.UseCount);
        Assert.Equal(Now.AddHours(2), state.UpdatedAt);
    }

    [Fact]
    public async Task AnEntryThatBecomesEmptyIsRemovedFromMemoryAndFile()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, true);
        await store.SetFlagAsync(Path.Combine(directory.Path, "b.png"), "hash-b", CardFlag.Liked, true);

        Assert.True(await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, false));

        Assert.Null(store.TryGetByPath(card));
        Assert.DoesNotContain(store.Snapshot(), s => s.State.Hash == "hash-a");
        Assert.DoesNotContain("hash-a", File.ReadAllText(CardStateStore.GetFilePath(directory.Path)));
        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        Assert.Equal("hash-b", Assert.Single(reloaded.Snapshot()).State.Hash);
    }

    [Fact]
    public async Task AUsedCardIsNeverEmptyEvenWithNoFlags()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.MarkUsedAsync(card, "hash-a");

        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, false);

        Assert.Equal(Now, store.TryGetByPath(card)?.LastUsedAt);
    }

    [Fact]
    public async Task SnapshotReturnsFullPaths()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "scenes");
        var second = Path.Combine(directory.Path, "characters");
        var store = Store();
        store.EnsureLoaded([first, second]);
        await store.SetFlagAsync(Path.Combine(first, "sub", "a.png"), "hash-a", CardFlag.Liked, true);
        await store.SetFlagAsync(Path.Combine(second, "b.png"), "hash-b", CardFlag.Queued, true);

        var reloaded = Store();
        reloaded.EnsureLoaded([first, second]);
        var paths = reloaded.Snapshot().Select(s => s.FullPath).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[] { Path.Combine(second, "b.png"), Path.Combine(first, "sub", "a.png") }.Order(StringComparer.Ordinal),
            paths);
    }

    [Fact]
    public async Task StateChangedFiresAfterTheFileIsWrittenWithTheFullPath()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "sub", "..", "a.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        string? changed = null;
        string? onDisk = null;
        store.StateChanged += path =>
        {
            changed = path;
            onDisk = File.ReadAllText(CardStateStore.GetFilePath(directory.Path));
        };

        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);

        Assert.Equal(Path.Combine(directory.Path, "a.png"), changed);
        Assert.Contains("hash-a", onDisk);
    }

    [Fact]
    public async Task AMovedCardIsFoundByItsHashAndItsPathIsRewritten()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "old", "a.png");
        var newPath = Path.Combine(directory.Path, "new", "renamed.png");
        var store = Store();
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(oldPath, "hash-a", CardFlag.Liked, true);

        Assert.Null(store.TryGetByPath(newPath));
        var resolved = await store.ResolveAsync(newPath, "hash-a");

        Assert.True(resolved?.Liked);
        Assert.Equal(Path.Combine("new", "renamed.png"), resolved!.RelativePath);
        Assert.NotNull(store.TryGetByPath(newPath));
        Assert.Null(store.TryGetByPath(oldPath));
        var reloaded = Store();
        reloaded.EnsureLoaded([directory.Path]);
        Assert.True(reloaded.TryGetByPath(newPath)?.Liked);
        Assert.Null(reloaded.TryGetByPath(oldPath));
    }

    [Fact]
    public async Task ResolvingAnUnknownHashReturnsNull()
    {
        using var directory = new TestDirectory();
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.Null(await store.ResolveAsync(Path.Combine(directory.Path, "a.png"), "hash-a"));
        Assert.False(File.Exists(CardStateStore.GetFilePath(directory.Path)));
    }

    [Fact]
    public async Task ACardMovedToAnotherRootMovesBetweenFiles()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first");
        var second = Path.Combine(directory.Path, "second");
        var store = Store();
        store.EnsureLoaded([first, second]);
        await store.SetFlagAsync(Path.Combine(first, "a.png"), "hash-a", CardFlag.Liked, true);

        var resolved = await store.ResolveAsync(Path.Combine(second, "a.png"), "hash-a");

        Assert.True(resolved?.Liked);
        Assert.DoesNotContain("hash-a", File.ReadAllText(CardStateStore.GetFilePath(first)));
        Assert.Contains("hash-a", File.ReadAllText(CardStateStore.GetFilePath(second)));
        Assert.True(store.TryGetByPath(Path.Combine(second, "a.png"))?.Liked);
        Assert.Null(store.TryGetByPath(Path.Combine(first, "a.png")));
    }

    [Fact]
    public async Task ADamagedFileIsMovedAsideAndTheStoreKeepsWorking()
    {
        using var directory = new TestDirectory();
        var file = CardStateStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ not json");
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.True(await store.SetFlagAsync(Path.Combine(directory.Path, "a.png"), "hash-a", CardFlag.Queued, true));

        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "card_states.json.unreadable-*"));
        Assert.Equal("{ not json", File.ReadAllText(aside));
        Assert.Contains("hash-a", File.ReadAllText(file));
    }

    [Fact]
    public async Task AFileFromANewerVersionIsMovedAside()
    {
        using var directory = new TestDirectory();
        var file = CardStateStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """{ "schemaVersion": 2, "states": [] }""");
        var store = Store();
        store.EnsureLoaded([directory.Path]);

        Assert.True(await store.SetFlagAsync(Path.Combine(directory.Path, "a.png"), "hash-a", CardFlag.Queued, true));

        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(file)!, "card_states.json.unreadable-*"));
    }

    [WindowsFact]
    public async Task ALockedFileMakesItsRootReadOnly()
    {
        using var directory = new TestDirectory();
        var file = CardStateStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{}");
        var store = Store();
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.EnsureLoaded([directory.Path]);

        Assert.False(await store.SetFlagAsync(Path.Combine(directory.Path, "a.png"), "hash-a", CardFlag.Liked, true));
        Assert.False(await store.MarkUsedAsync(Path.Combine(directory.Path, "a.png"), "hash-a"));
        Assert.Equal("{}", File.ReadAllText(file));
        Assert.Empty(store.Snapshot());
    }

    [Fact]
    public async Task ACardOutsideEveryRootCannotBeChanged()
    {
        using var directory = new TestDirectory();
        var store = Store();
        store.EnsureLoaded([Path.Combine(directory.Path, "library")]);

        Assert.False(await store.SetFlagAsync(Path.Combine(directory.Path, "elsewhere", "a.png"), "hash-a", CardFlag.Liked, true));
        Assert.False(await store.SetFlagAsync(Path.Combine(directory.Path, "library-2", "a.png"), "hash-a", CardFlag.Liked, true));
        Assert.False(await store.MarkUsedAsync(Path.Combine(directory.Path, "elsewhere", "a.png"), "hash-a"));
        Assert.Empty(store.Snapshot());
    }
}

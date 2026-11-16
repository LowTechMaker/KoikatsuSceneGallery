using System.Text.Json;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// <see cref="CardState.FiledRating"/>: the rating a card was filed under, kept
/// as data so it survives leaving the rating folder that used to say it.
/// </summary>
public sealed class CardStateFiledRatingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static CardStateStore Store() => new(() => Now);

    private static CardStateStore Reload(string root)
    {
        var store = Store();
        store.EnsureLoaded([root]);
        return store;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AFiledRatingAloneKeepsTheEntryAndSurvivesAReload(int rating)
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Reload(directory.Path);

        Assert.True(await store.SetFiledRatingAsync(card, "hash-a", rating));

        var state = Reload(directory.Path).TryGetByPath(card);
        Assert.NotNull(state);
        Assert.Equal(rating, state.FiledRating);
        Assert.False(state.Queued || state.Liked || state.Hidden);
        Assert.Null(state.LastUsedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(null)]
    public async Task GeneralOrNoFiledRatingCarriesNothing(int? rating)
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Reload(directory.Path);

        Assert.True(await store.SetFiledRatingAsync(card, "hash-a", rating));

        Assert.Null(store.TryGetByPath(card));
        Assert.False(File.Exists(CardStateStore.GetFilePath(directory.Path)));
    }

    [Fact]
    public async Task ClearingTheOnlyFiledRatingRemovesTheEntry()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Reload(directory.Path);
        await store.SetFiledRatingAsync(card, "hash-a", 1);

        await store.SetFiledRatingAsync(card, "hash-a", 0);

        Assert.Null(store.TryGetByPath(card));
        Assert.DoesNotContain("hash-a", File.ReadAllText(CardStateStore.GetFilePath(directory.Path)));
    }

    [Fact]
    public async Task AGeneralFiledRatingIsKeptBesideFlags()
    {
        // A card explicitly filed as G (chosen in the re-identify dialog) on
        // an entry that exists anyway keeps that choice.
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Reload(directory.Path);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);

        await store.SetFiledRatingAsync(card, "hash-a", 0);

        Assert.Equal(0, Reload(directory.Path).TryGetByPath(card)?.FiledRating);
    }

    [Fact]
    public async Task FlagsAndUseDoNotTouchTheFiledRating()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var store = Reload(directory.Path);
        await store.SetFiledRatingAsync(card, "hash-a", 2);

        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, true);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Hidden, true);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);
        await store.MarkUsedAsync(card, "hash-a");
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, false);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Hidden, false);

        Assert.Equal(2, store.TryGetByPath(card)?.FiledRating);
    }

    [Fact]
    public async Task TheFiledRatingDoesNotTouchFlagsOrTheShelfOrder()
    {
        using var directory = new TestDirectory();
        var card = Path.Combine(directory.Path, "a.png");
        var clock = Now;
        var store = new CardStateStore(() => clock);
        store.EnsureLoaded([directory.Path]);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Queued, true);
        await store.SetFlagAsync(card, "hash-a", CardFlag.Liked, true);

        clock = Now.AddDays(1);
        await store.SetFiledRatingAsync(card, "hash-a", 1);

        var state = store.TryGetByPath(card)!;
        Assert.True(state.Queued && state.Liked);
        // UpdatedAt orders the "want to try" and favourites shelves: filing
        // thousands of ratings must not reshuffle them.
        Assert.Equal(Now, state.UpdatedAt);
    }

    [Fact]
    public void AFileWithoutTheFieldLoadsAndOutOfRangeValuesReadAsNone()
    {
        using var directory = new TestDirectory();
        var file = CardStateStore.GetFilePath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, """
            { "schemaVersion": 1, "states": [
              { "hash": "old", "relativePath": "old.png", "queued": true, "liked": false, "hidden": false,
                "lastUsedAt": null, "useCount": 0, "updatedAt": "2026-11-15T12:00:00+00:00" },
              { "hash": "bad", "relativePath": "bad.png", "queued": true, "liked": false, "hidden": false,
                "lastUsedAt": null, "useCount": 0, "updatedAt": "2026-11-15T12:00:00+00:00", "filedRating": 7 },
              { "hash": "neg", "relativePath": "neg.png", "queued": false, "liked": false, "hidden": false,
                "lastUsedAt": null, "useCount": 0, "updatedAt": "2026-11-15T12:00:00+00:00", "filedRating": -1 }
            ] }
            """);

        var store = Reload(directory.Path);

        var old = store.TryGetByPath(Path.Combine(directory.Path, "old.png"));
        Assert.True(old?.Queued);
        Assert.Null(old!.FiledRating);
        Assert.Null(store.TryGetByPath(Path.Combine(directory.Path, "bad.png"))?.FiledRating);
        // An entry whose only content was an out-of-range rating has nothing left.
        Assert.Null(store.TryGetByPath(Path.Combine(directory.Path, "neg.png")));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!, "*.unreadable-*"));
    }

    [Fact]
    public async Task NoFiledRatingIsNotWritten()
    {
        using var directory = new TestDirectory();
        var store = Reload(directory.Path);
        await store.SetFlagAsync(Path.Combine(directory.Path, "a.png"), "hash-a", CardFlag.Queued, true);
        await store.SetFiledRatingAsync(Path.Combine(directory.Path, "b.png"), "hash-b", 1);

        using var json = JsonDocument.Parse(File.ReadAllText(CardStateStore.GetFilePath(directory.Path)));
        var entries = json.RootElement.GetProperty("states").EnumerateArray()
            .ToDictionary(e => e.GetProperty("hash").GetString()!);
        Assert.False(entries["hash-a"].TryGetProperty("filedRating", out _));
        Assert.Equal(1, entries["hash-b"].GetProperty("filedRating").GetInt32());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task AnUnknownRatingIsRejected(int rating)
    {
        using var directory = new TestDirectory();
        var store = Reload(directory.Path);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetFiledRatingAsync(Path.Combine(directory.Path, "a.png"), "hash-a", rating));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SetFiledRatingsAsync([(Path.Combine(directory.Path, "a.png"), "hash-a", rating)]));
        Assert.False(File.Exists(CardStateStore.GetFilePath(directory.Path)));
    }

    [Fact]
    public async Task TheFiledRatingFollowsAMovedCard()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "R-18", "a.png");
        var newPath = Path.Combine(directory.Path, "author", "a.png");
        var store = Reload(directory.Path);
        await store.SetFiledRatingAsync(oldPath, "hash-a", 1);

        Assert.Equal(1, (await store.ResolveAsync(newPath, "hash-a"))?.FiledRating);
        Assert.Equal(1, Reload(directory.Path).TryGetByPath(newPath)?.FiledRating);
    }

    // --- batch ---

    [Fact]
    public async Task ABatchWritesEachRootOnceAndAnnouncesEveryCardAfterward()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "scenes");
        var second = Path.Combine(directory.Path, "characters");
        var store = Store();
        store.EnsureLoaded([first, second]);
        var batch = Enumerable.Range(0, 50)
            .Select(i => (Path.Combine(i % 2 == 0 ? first : second, $"{i}.png"), $"hash-{i}", 1 + i % 2))
            .ToList();
        var announced = new List<string>();
        var writesWhenAnnounced = new List<int>();
        store.StateChanged += path =>
        {
            announced.Add(path);
            writesWhenAnnounced.Add(store.WriteCount);
        };

        Assert.Equal(0, await store.SetFiledRatingsAsync(batch));

        Assert.Equal(2, store.WriteCount);
        Assert.Equal(batch.Select(b => b.Item1).Order(), announced.Order());
        Assert.All(writesWhenAnnounced, count => Assert.Equal(2, count));
        var reloaded = Store();
        reloaded.EnsureLoaded([first, second]);
        Assert.All(batch, b => Assert.Equal(b.Item3, reloaded.TryGetByPath(b.Item1)?.FiledRating));
    }

    [Fact]
    public async Task ABatchCountsWhatItCouldNotStoreAndStoresTheRest()
    {
        using var directory = new TestDirectory();
        var root = Path.Combine(directory.Path, "lib");
        var store = Store();
        store.EnsureLoaded([root]);

        var failed = await store.SetFiledRatingsAsync(
        [
            (Path.Combine(root, "a.png"), "hash-a", 1),
            (Path.Combine(directory.Path, "elsewhere", "b.png"), "hash-b", 1),
            (Path.Combine(root, "c.png"), "hash-c", 2),
        ]);

        Assert.Equal(1, failed);
        Assert.Equal(1, store.TryGetByPath(Path.Combine(root, "a.png"))?.FiledRating);
        Assert.Equal(2, store.TryGetByPath(Path.Combine(root, "c.png"))?.FiledRating);
        Assert.Equal(1, store.WriteCount);
    }

    [WindowsFact]
    public async Task ABatchIntoAReadOnlyRootStoresNothingThere()
    {
        using var directory = new TestDirectory();
        var locked = Path.Combine(directory.Path, "locked");
        var file = CardStateStore.GetFilePath(locked);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{}");
        var store = Store();
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            store.EnsureLoaded([locked]);

        Assert.Equal(1, await store.SetFiledRatingsAsync([(Path.Combine(locked, "a.png"), "hash-a", 1)]));
        Assert.Equal("{}", File.ReadAllText(file));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task AnEmptyBatchWritesNothing()
    {
        using var directory = new TestDirectory();
        var store = Reload(directory.Path);

        Assert.Equal(0, await store.SetFiledRatingsAsync([]));
        Assert.Equal(0, store.WriteCount);
    }

    [Fact]
    public async Task ABatchMovingAnEntryToAnotherRootRewritesBothFilesOnce()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first");
        var second = Path.Combine(directory.Path, "second");
        var store = Store();
        store.EnsureLoaded([first, second]);
        await store.SetFlagAsync(Path.Combine(first, "a.png"), "hash-a", CardFlag.Liked, true);
        var before = store.WriteCount;

        await store.SetFiledRatingsAsync(
        [
            (Path.Combine(second, "a.png"), "hash-a", 1),
            (Path.Combine(second, "b.png"), "hash-b", 1),
        ]);

        Assert.Equal(2, store.WriteCount - before);
        Assert.DoesNotContain("hash-a", File.ReadAllText(CardStateStore.GetFilePath(first)));
        Assert.True(store.TryGetByPath(Path.Combine(second, "a.png"))?.Liked);
    }
}

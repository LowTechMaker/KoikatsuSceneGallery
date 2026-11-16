using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardStateReconcilerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A hash function over a fixed path → hash map that counts its calls.</summary>
    private sealed class FakeHashes(Dictionary<string, string> hashes)
    {
        public List<string> Calls { get; } = [];
        public Action<string>? OnCall { get; set; }

        public string? Compute(string path, CancellationToken cancellationToken)
        {
            Calls.Add(path);
            OnCall?.Invoke(path);
            return hashes.TryGetValue(path, out var hash) ? hash : null;
        }
    }

    private static async Task<CardStateStore> StoreWith(string root, params (string Path, string Hash)[] queued)
        => await StoreWith([root], queued);

    private static async Task<CardStateStore> StoreWith(string[] roots, params (string Path, string Hash)[] queued)
    {
        var store = new CardStateStore(() => Now);
        store.EnsureLoaded(roots);
        foreach (var (path, hash) in queued)
            Assert.True(await store.SetFlagAsync(path, hash, CardFlag.Queued, true));
        return store;
    }

    private static CardStateReconciler Reconciler(CardStateStore store, FakeHashes hashes)
        => new(store, hashes.Compute);

    [Fact]
    public async Task ACardMovedWithinARootIsReattached()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "a.png");
        var newPath = Path.Combine(directory.Path, "sub", "a.png");
        var store = await StoreWith(directory.Path, (oldPath, "hash-a"));
        var hashes = new FakeHashes(new() { [newPath] = "hash-a" });

        Assert.Equal(1, await Reconciler(store, hashes).ReconcileAsync([newPath]));

        Assert.True(store.TryGetByPath(newPath)?.Queued);
        Assert.Null(store.TryGetByPath(oldPath));
        var reloaded = new CardStateStore(() => Now);
        reloaded.EnsureLoaded([directory.Path]);
        Assert.Equal(Path.Combine("sub", "a.png"), reloaded.TryGetByPath(newPath)?.RelativePath);
    }

    [Fact]
    public async Task ACardRenamedAndMovedIsReattachedAndItsChangeAnnounced()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "fake_scene_15.png");
        var newPath = Path.Combine(directory.Path, "moved", "renamed.png");
        var other = Path.Combine(directory.Path, "other.png");
        var store = await StoreWith(directory.Path, (oldPath, "hash-a"));
        var hashes = new FakeHashes(new() { [newPath] = "hash-a", [other] = "hash-other" });
        var changed = new List<string>();
        store.StateChanged += changed.Add;

        Assert.Equal(1, await Reconciler(store, hashes).ReconcileAsync([other, newPath]));

        Assert.True(store.TryGetByPath(newPath)?.Queued);
        Assert.Equal([newPath], changed);
    }

    [Fact]
    public async Task ACardMovedToAnotherRootIsReattachedThere()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first");
        var second = Path.Combine(directory.Path, "second");
        var oldPath = Path.Combine(first, "a.png");
        var newPath = Path.Combine(second, "a.png");
        // The old root still has a card on disk, so its library is loaded.
        var stays = Path.Combine(first, "stays.png");
        var store = await StoreWith([first, second], (oldPath, "hash-a"));
        var hashes = new FakeHashes(new() { [newPath] = "hash-a" });

        Assert.Equal(1, await Reconciler(store, hashes).ReconcileAsync([stays, newPath]));

        Assert.True(store.TryGetByPath(newPath)?.Queued);
        Assert.DoesNotContain("hash-a", File.ReadAllText(CardStateStore.GetFilePath(first)));
        Assert.Contains("hash-a", File.ReadAllText(CardStateStore.GetFilePath(second)));
    }

    [Fact]
    public async Task TwoOrphansOneMatchReattachesOneAndKeepsTheOther()
    {
        using var directory = new TestDirectory();
        var a = Path.Combine(directory.Path, "a.png");
        var b = Path.Combine(directory.Path, "b.png");
        var movedA = Path.Combine(directory.Path, "x", "a.png");
        var store = await StoreWith(directory.Path, (a, "hash-a"), (b, "hash-b"));
        var hashes = new FakeHashes(new() { [movedA] = "hash-a" });

        Assert.Equal(1, await Reconciler(store, hashes).ReconcileAsync([movedA]));

        Assert.True(store.TryGetByPath(movedA)?.Queued);
        // b may be on a drive that is not mounted: its state stays where it was.
        Assert.True(store.TryGetByPath(b)?.Queued);
        Assert.Equal(2, store.Snapshot().Count);
    }

    [Fact]
    public async Task NoOrphansMeansNothingIsHashed()
    {
        using var directory = new TestDirectory();
        var a = Path.Combine(directory.Path, "a.png");
        var store = await StoreWith(directory.Path, (a, "hash-a"));
        var hashes = new FakeHashes([]);

        Assert.Equal(0, await Reconciler(store, hashes).ReconcileAsync(
            [a, Path.Combine(directory.Path, "b.png"), Path.Combine(directory.Path, "c.png")]));

        Assert.Empty(hashes.Calls);
    }

    [Fact]
    public async Task AnUnmatchedOrphanIsKeptAndOnFile()
    {
        using var directory = new TestDirectory();
        var a = Path.Combine(directory.Path, "a.png");
        var store = await StoreWith(directory.Path, (a, "hash-a"));
        var unrelated = Path.Combine(directory.Path, "b.png");
        var hashes = new FakeHashes(new() { [unrelated] = "hash-b" });

        Assert.Equal(0, await Reconciler(store, hashes).ReconcileAsync([unrelated]));

        Assert.True(store.TryGetByPath(a)?.Queued);
        Assert.Contains("hash-a", File.ReadAllText(CardStateStore.GetFilePath(directory.Path)));
    }

    [Fact]
    public async Task AnOrphanInARootWithNoLiveCardIsNotSearchedFor()
    {
        // The root's library has not loaded yet, or its drive is not mounted:
        // every state there looks orphaned, and hashing the other libraries'
        // cards to look for them would be wasted.
        using var directory = new TestDirectory();
        var scenes = Path.Combine(directory.Path, "scenes");
        var characters = Path.Combine(directory.Path, "characters");
        var character = Path.Combine(characters, "chara.png");
        var store = await StoreWith([scenes, characters], (character, "hash-chara"));
        var scene = Path.Combine(scenes, "scene.png");
        var hashes = new FakeHashes(new() { [scene] = "hash-scene" });

        Assert.Equal(0, await Reconciler(store, hashes).ReconcileAsync([scene]));

        Assert.Empty(hashes.Calls);
        Assert.True(store.TryGetByPath(character)?.Queued);
    }

    [Fact]
    public async Task OnlyCardsWithoutStoredStateAreHashed()
    {
        using var directory = new TestDirectory();
        var orphan = Path.Combine(directory.Path, "gone.png");
        var known = Path.Combine(directory.Path, "known.png");
        var fresh = Path.Combine(directory.Path, "fresh.png");
        var store = await StoreWith(directory.Path, (orphan, "hash-gone"), (known, "hash-known"));
        var hashes = new FakeHashes(new() { [fresh] = "hash-fresh" });

        await Reconciler(store, hashes).ReconcileAsync([known, fresh]);

        Assert.Equal([fresh], hashes.Calls);
    }

    [Fact]
    public async Task LikelyCandidatesAreHashedFirstAndTheSearchStopsWhenAllAreMatched()
    {
        using var directory = new TestDirectory();
        var orphan = Path.Combine(directory.Path, "author", "scene.png");
        var store = await StoreWith(directory.Path, (orphan, "hash-a"));
        var sameName = Path.Combine(directory.Path, "moved", "scene.png");
        var sameDirectory = Path.Combine(directory.Path, "author", "other.png");
        var rest = Enumerable.Range(0, 20).Select(i => Path.Combine(directory.Path, "z", $"{i}.png")).ToArray();
        var hashes = new FakeHashes(new() { [sameName] = "hash-a" });

        await Reconciler(store, hashes).ReconcileAsync([.. rest, sameDirectory, sameName]);

        Assert.Equal([sameName], hashes.Calls);
    }

    [Fact]
    public async Task TheSameDirectoryIsTriedBeforeTheRest()
    {
        using var directory = new TestDirectory();
        var orphan = Path.Combine(directory.Path, "author", "scene.png");
        var store = await StoreWith(directory.Path, (orphan, "hash-a"));
        var renamedInPlace = Path.Combine(directory.Path, "author", "renamed.png");
        var rest = Enumerable.Range(0, 20).Select(i => Path.Combine(directory.Path, "z", $"{i}.png")).ToArray();
        var hashes = new FakeHashes(new() { [renamedInPlace] = "hash-a" });

        await Reconciler(store, hashes).ReconcileAsync([.. rest, renamedInPlace]);

        Assert.Equal([renamedInPlace], hashes.Calls);
        Assert.True(store.TryGetByPath(renamedInPlace)?.Queued);
    }

    [Fact]
    public async Task ASecondRunDoesNotHashTheSamePathsAgain()
    {
        using var directory = new TestDirectory();
        var orphan = Path.Combine(directory.Path, "gone.png");
        var store = await StoreWith(directory.Path, (orphan, "hash-gone"));
        var live = Enumerable.Range(0, 5).Select(i => Path.Combine(directory.Path, $"{i}.png")).ToArray();
        var hashes = new FakeHashes(live.ToDictionary(p => p, p => "hash-" + Path.GetFileName(p)));
        var reconciler = Reconciler(store, hashes);

        await reconciler.ReconcileAsync(live);
        Assert.Equal(5, hashes.Calls.Count);
        await reconciler.ReconcileAsync(live);

        Assert.Equal(5, hashes.Calls.Count);
    }

    [Fact]
    public async Task AHashFromAnEarlierRunStillMatchesALaterOrphan()
    {
        using var directory = new TestDirectory();
        var first = Path.Combine(directory.Path, "first.png");
        var second = Path.Combine(directory.Path, "second.png");
        var store = await StoreWith(directory.Path, (first, "hash-first"));
        var hashes = new FakeHashes(new() { [second] = "hash-second" });
        var reconciler = Reconciler(store, hashes);
        // First run: "first" is orphaned, "second" is hashed and does not match.
        await reconciler.ReconcileAsync([second]);

        // The card now at "second" gets a state under another path that then
        // disappears; the remembered hash must still find it without hashing.
        var elsewhere = Path.Combine(directory.Path, "elsewhere.png");
        await store.SetFlagAsync(elsewhere, "hash-second", CardFlag.Liked, true);
        Assert.Equal(1, await reconciler.ReconcileAsync([second]));

        Assert.True(store.TryGetByPath(second)?.Liked);
        Assert.Single(hashes.Calls);
    }

    [Fact]
    public async Task CancellationStopsTheSearchAndKeepsWhatWasHashed()
    {
        using var directory = new TestDirectory();
        var orphan = Path.Combine(directory.Path, "gone.png");
        var store = await StoreWith(directory.Path, (orphan, "hash-gone"));
        var live = Enumerable.Range(0, 5).Select(i => Path.Combine(directory.Path, $"{i}.png")).ToArray();
        var hashes = new FakeHashes([]);
        using var cancel = new CancellationTokenSource();
        hashes.OnCall = _ => cancel.Cancel();
        var reconciler = Reconciler(store, hashes);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reconciler.ReconcileAsync(live, cancel.Token));
        Assert.Single(hashes.Calls);

        hashes.OnCall = null;
        await reconciler.ReconcileAsync(live);
        Assert.Equal(5, hashes.Calls.Count);
        Assert.True(store.TryGetByPath(orphan)?.Queued);
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenHashesNothing()
    {
        using var directory = new TestDirectory();
        var store = await StoreWith(directory.Path, (Path.Combine(directory.Path, "gone.png"), "hash-gone"));
        var hashes = new FakeHashes([]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reconciler(store, hashes)
            .ReconcileAsync([Path.Combine(directory.Path, "a.png")], new CancellationToken(canceled: true)));

        Assert.Empty(hashes.Calls);
    }

    [Fact]
    public async Task AnEntryWithOnlyAFiledRatingIsReattachedAfterAMove()
    {
        using var directory = new TestDirectory();
        var oldPath = Path.Combine(directory.Path, "R-18", "a.png");
        var newPath = Path.Combine(directory.Path, "author", "a.png");
        var store = await StoreWith(directory.Path);
        Assert.True(await store.SetFiledRatingAsync(oldPath, "hash-a", 1));
        var hashes = new FakeHashes(new() { [newPath] = "hash-a" });

        Assert.Equal(1, await Reconciler(store, hashes).ReconcileAsync([newPath]));

        Assert.Equal(1, store.TryGetByPath(newPath)?.FiledRating);
    }
}

using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class SidecarBackupStoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 5, 0, 0, TimeSpan.Zero);

    private readonly string _root = Directory.CreateTempSubdirectory("sidecar-backup").FullName;
    private readonly string _library;
    private readonly SidecarBackupStore _store;

    public SidecarBackupStoreTests()
    {
        _library = Path.Combine(_root, "library");
        Directory.CreateDirectory(_library);
        _store = new SidecarBackupStore(Path.Combine(_root, "backups"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Sidecar(string name, string? content = null)
    {
        var path = Path.Combine(_library, name);
        if (content is not null) File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task RestoringPutsOverwrittenSidecarsBack()
    {
        var sidecar = Sidecar("pixiv_1.json", "before");
        var backup = await _store.CreateAsync("pixiv", [sidecar], T0, default);

        File.WriteAllText(sidecar, "after a bad run");
        var result = await _store.RestoreAsync(backup, default);

        Assert.Equal("before", File.ReadAllText(sidecar));
        Assert.Equal(1, result.Restored);
    }

    [Fact]
    public async Task RestoringRemovesSidecarsTheRunCreated()
    {
        // No earlier content exists, so undoing the run means the file goes.
        var created = Sidecar("pixiv_2.json");
        var backup = await _store.CreateAsync("pixiv", [created], T0, default);
        Assert.Equal(1, backup.Absent);

        File.WriteAllText(created, "written by the run");
        var result = await _store.RestoreAsync(backup, default);

        Assert.False(File.Exists(created));
        Assert.Equal(1, result.Removed);
    }

    [Fact]
    public async Task OnlyTheTwoNewestBackupsAreKept()
    {
        var sidecar = Sidecar("pixiv_1.json", "v1");
        await _store.CreateAsync("pixiv", [sidecar], T0, default);
        File.WriteAllText(sidecar, "v2");
        await _store.CreateAsync("pixiv", [sidecar], T0.AddHours(1), default);
        File.WriteAllText(sidecar, "v3");
        await _store.CreateAsync("pixiv", [sidecar], T0.AddHours(2), default);

        var backups = _store.List("pixiv");

        Assert.Equal(SidecarBackupStore.Slots, backups.Count);
        Assert.Equal([T0.AddHours(2), T0.AddHours(1)], backups.Select(b => b.CreatedAt));
    }

    [Fact]
    public async Task TheOlderSlotStillHoldsTheStateBeforeASecondRun()
    {
        // The reason for two slots: a second run started over bad data must
        // not replace the last good copy with the bad one.
        var sidecar = Sidecar("pixiv_1.json", "good");
        await _store.CreateAsync("pixiv", [sidecar], T0, default);
        File.WriteAllText(sidecar, "bad");
        await _store.CreateAsync("pixiv", [sidecar], T0.AddHours(1), default);
        File.WriteAllText(sidecar, "worse");

        await _store.RestoreAsync(_store.List("pixiv")[^1], default);

        Assert.Equal("good", File.ReadAllText(sidecar));
    }

    [Fact]
    public async Task ABackupCutShortIsNeitherListedNorKept()
    {
        var sidecar = Sidecar("pixiv_1.json", "x");
        var incomplete = Path.Combine(_root, "backups", "pixiv", "20260101T000000Z");
        Directory.CreateDirectory(Path.Combine(incomplete, "files"));

        Assert.Empty(_store.List("pixiv"));
        await _store.CreateAsync("pixiv", [sidecar], T0, default);

        Assert.False(Directory.Exists(incomplete));
        Assert.Single(_store.List("pixiv"));
    }

    [Fact]
    public async Task ExtendingCoversNewPathsWithoutTouchingWhatWasSaved()
    {
        var first = Sidecar("pixiv_1.json", "before");
        var backup = await _store.CreateAsync("pixiv", [first], T0, default);
        File.WriteAllText(first, "changed by the run");

        // Imported after the run began; the run never touched it.
        var later = Sidecar("pixiv_3.json", "imported later");
        backup = await _store.ExtendAsync(backup, [first, later], default);
        Assert.Equal(2, backup.Saved);

        File.WriteAllText(later, "changed by the resumed run");
        await _store.RestoreAsync(backup, default);

        Assert.Equal("before", File.ReadAllText(first));
        Assert.Equal("imported later", File.ReadAllText(later));
    }

    [Fact]
    public async Task PlatformsKeepSeparateBackups()
    {
        await _store.CreateAsync("pixiv", [Sidecar("a.json", "a")], T0, default);
        Assert.Empty(_store.List("fanbox"));
    }

    [Fact]
    public async Task TheSamePathNamedTwiceIsSavedOnce()
    {
        var sidecar = Sidecar("pixiv_1.json", "x");
        var backup = await _store.CreateAsync("pixiv", [sidecar, sidecar], T0, default);
        Assert.Equal(1, backup.Saved);
    }
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// What the galleries learn when a folder is renamed underneath them.
/// </summary>
/// <remarks>
/// Renaming a local source renames its folder, which changes the path of every
/// card inside it. FileSystemWatcher reports that as one event for the
/// directory and none for the files, so without expansion the galleries would
/// keep the old paths — thumbnails, detail pages and the file-in-explorer
/// command all pointing at files that no longer exist.
/// </remarks>
public sealed class CardWatcherRenameTests
{
    /// <summary>Debounce is 300 ms; this is generous room around it.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ksg-watch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Card(string relativePath)
        {
            var full = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            using var stream = File.Create(full);
            stream.Write(Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
            return full;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static async Task<bool> WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Settle;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(50);
        }

        return condition();
    }

    [Fact]
    public async Task RenamingAFolderRefilesEveryCardUnderIt()
    {
        using var root = new TempRoot();
        var before = root.Card(Path.Combine("阿明 (local-k7f3q9)", "card.png"));
        var alsoBefore = root.Card(Path.Combine("阿明 (local-k7f3q9)", "nested", "other.png"));

        using var service = new CharacterCardService();
        var added = new List<CharacterCard>();
        var removed = new List<string>();
        service.CardAdded += card => { lock (added) added.Add(card); };
        service.CardRemoved += path => { lock (removed) removed.Add(path); };
        service.StartWatching([root.Path]);

        var renamed = Path.Combine(root.Path, "小明 (local-k7f3q9)");
        Directory.Move(Path.Combine(root.Path, "阿明 (local-k7f3q9)"), renamed);

        var landed = await WaitUntil(() =>
        {
            lock (added) lock (removed) return added.Count >= 2 && removed.Count >= 2;
        });

        Assert.True(landed, $"added={added.Count} removed={removed.Count}");
        lock (removed)
        {
            Assert.Contains(before, removed);
            Assert.Contains(alsoBefore, removed);
        }

        lock (added)
        {
            Assert.Contains(added, card => card.FilePath == Path.Combine(renamed, "card.png"));
            Assert.Contains(
                added,
                card => card.FilePath == Path.Combine(renamed, "nested", "other.png"));
        }
    }

    // A renamed file still has to behave as it always did.
    [Fact]
    public async Task RenamingASingleCardStillReportsJustThatCard()
    {
        using var root = new TempRoot();
        var before = root.Card(Path.Combine("folder", "card.png"));

        using var service = new CharacterCardService();
        var added = new List<CharacterCard>();
        var removed = new List<string>();
        service.CardAdded += card => { lock (added) added.Add(card); };
        service.CardRemoved += path => { lock (removed) removed.Add(path); };
        service.StartWatching([root.Path]);

        var after = Path.Combine(root.Path, "folder", "renamed.png");
        File.Move(before, after);

        var landed = await WaitUntil(() =>
        {
            lock (added) lock (removed) return added.Count >= 1 && removed.Count >= 1;
        });

        Assert.True(landed, $"added={added.Count} removed={removed.Count}");
        lock (removed) Assert.Contains(before, removed);
        lock (added) Assert.Contains(added, card => card.FilePath == after);
    }
}

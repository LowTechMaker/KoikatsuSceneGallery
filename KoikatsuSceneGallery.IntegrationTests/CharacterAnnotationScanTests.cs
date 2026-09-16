using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// Annotations are read on the scan path, which runs over every card in the
/// library. The cost has to be per directory, not per card.
/// </summary>
public sealed class CharacterAnnotationScanTests
{
    /// <summary>Counts how many times a document is actually read from disk.</summary>
    private sealed class CountingStore : CharacterAnnotationStore
    {
        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        protected override CharacterAnnotationDocument? ReadDocument(string documentPath)
        {
            Interlocked.Increment(ref _reads);
            return base.ReadDocument(documentPath);
        }
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ksg-annotation-" + Guid.NewGuid().ToString("N"));
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

    // The whole reason the sidecar is one file per directory rather than one
    // per card.
    [Fact]
    public async Task ScanningReadsOneDocumentPerDirectoryNotPerCard()
    {
        using var root = new TempRoot();
        foreach (var i in Enumerable.Range(0, 20))
            root.Card(Path.Combine("folderA", $"card{i}.png"));
        foreach (var i in Enumerable.Range(0, 20))
            root.Card(Path.Combine("folderB", $"card{i}.png"));

        var store = new CountingStore();
        var service = new CharacterCardService(store);

        var cards = await service.ScanFoldersAsync([root.Path]);

        Assert.Equal(40, cards.Count);
        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task ScannedCardsCarryTheirAnnotations()
    {
        using var root = new TempRoot();
        var marked = root.Card(Path.Combine("folder", "marked.png"));
        var plain = root.Card(Path.Combine("folder", "plain.png"));

        var store = new CharacterAnnotationStore();
        await store.UpdateAsync(marked, CharacterVersionKind.Alternate, superseded: false, "短髮 IF", "夜羽 咲");
        store.ClearCache();

        var cards = await new CharacterCardService(store).ScanFoldersAsync([root.Path]);

        var markedCard = Assert.Single(cards, card => card.FilePath == marked);
        Assert.Equal(CharacterVersionKind.Alternate, markedCard.VersionKind);
        Assert.True(markedCard.IsAlternateVersion);
        Assert.Equal("短髮 IF", markedCard.VersionNote);
        Assert.Equal("夜羽 咲", markedCard.CharacterGroupKey);

        var plainCard = Assert.Single(cards, card => card.FilePath == plain);
        Assert.Equal(CharacterVersionKind.Current, plainCard.VersionKind);
        Assert.False(plainCard.IsAlternateVersion);
        Assert.Null(plainCard.VersionNote);
    }

    // The service is constructed without a store in some paths; cards must
    // still be produced.
    [Fact]
    public async Task ScanningWorksWithNoStoreAtAll()
    {
        using var root = new TempRoot();
        root.Card(Path.Combine("folder", "card.png"));

        var cards = await new CharacterCardService().ScanFoldersAsync([root.Path]);

        Assert.Equal(CharacterVersionKind.Current, Assert.Single(cards).VersionKind);
    }
}

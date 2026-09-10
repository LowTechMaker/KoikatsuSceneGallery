using System.Text.Json;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// The first place in the app where something the user typed is persisted, so
/// the failure modes matter more than usual: a damaged file must cost the
/// directory its annotations, never the scan, and an unknown value must cost
/// one card, never the directory.
/// </summary>
public sealed class CharacterAnnotationStoreTests
{
    private static string Card(TestDirectory directory, string name)
    {
        var path = Path.Combine(directory.Path, name);
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public async Task WriteAndRead_RoundTripsInTheHiddenMetadataDirectory()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "阿明_01.png");
        var store = new CharacterAnnotationStore();

        await store.UpdateAsync(card, CharacterVersionKind.Alternate, false, "短髮 IF", "夜羽 咲");

        var path = store.GetDocumentPath(directory.Path);
        Assert.Equal(Path.Combine(directory.Path, ".scenegallery", "characters.json"), path);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.True(File.GetAttributes(Path.GetDirectoryName(path)!).HasFlag(FileAttributes.Hidden));

        var entry = store.GetForCard(card);
        Assert.NotNull(entry);
        Assert.Equal(CharacterVersionKind.Alternate, entry.ParsedKind);
        Assert.Equal("短髮 IF", entry.Note);
        Assert.Equal("夜羽 咲", entry.GroupKey);
        Assert.Equal(3, entry.FileSize);

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("alternate", json.RootElement.GetProperty("cards")
            .GetProperty("阿明_01.png").GetProperty("label").GetString());
    }

    [Fact]
    public async Task GetForCard_IsCaseInsensitiveOnTheFileName()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "Chara.png");
        var store = new CharacterAnnotationStore();
        await store.UpdateAsync(card, CharacterVersionKind.Current, true, null, null);

        Assert.NotNull(store.GetForCard(Path.Combine(directory.Path, "CHARA.PNG")));
    }

    [Fact]
    public void GetForCard_ReturnsNullWhenTheDirectoryHasNoDocument()
    {
        using var directory = new TestDirectory();

        Assert.Null(new CharacterAnnotationStore().GetForCard(Card(directory, "chara.png")));
    }

    // Several cards in one directory share one document, so an edit to one
    // must not drop the others.
    [Fact]
    public async Task UpdatingOneCardKeepsTheOthersInTheSameDirectory()
    {
        using var directory = new TestDirectory();
        var first = Card(directory, "a.png");
        var second = Card(directory, "b.png");
        var store = new CharacterAnnotationStore();

        await store.UpdateAsync(first, CharacterVersionKind.Current, true, "舊的", null);
        await store.UpdateAsync(second, CharacterVersionKind.Alternate, false, null, "某人");

        Assert.Equal(CharacterVersionKind.Current, store.GetForCard(first)?.ParsedKind);
        Assert.Equal("舊的", store.GetForCard(first)?.Note);
        Assert.Equal(CharacterVersionKind.Alternate, store.GetForCard(second)?.ParsedKind);
    }

    [Fact]
    public async Task ClearingAnAnnotationRemovesTheEntryAndFinallyTheDocument()
    {
        using var directory = new TestDirectory();
        var first = Card(directory, "a.png");
        var second = Card(directory, "b.png");
        var store = new CharacterAnnotationStore();
        await store.UpdateAsync(first, CharacterVersionKind.Current, true, null, null);
        await store.UpdateAsync(second, CharacterVersionKind.Current, true, null, null);

        Assert.Null(await store.UpdateAsync(first, CharacterVersionKind.Current, false, null, null));
        Assert.Null(store.GetForCard(first));
        Assert.NotNull(store.GetForCard(second));
        Assert.True(File.Exists(store.GetDocumentPath(directory.Path)));

        await store.UpdateAsync(second, CharacterVersionKind.Current, false, "   ", "");

        Assert.Null(store.GetForCard(second));
        Assert.False(File.Exists(store.GetDocumentPath(directory.Path)));
    }

    [Fact]
    public async Task CreatedAtSurvivesLaterEdits()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png");
        var store = new CharacterAnnotationStore();
        await store.UpdateAsync(card, CharacterVersionKind.Current, true, null, null);
        var path = store.GetDocumentPath(directory.Path);
        var created = JsonDocument.Parse(File.ReadAllText(path)).RootElement
            .GetProperty("createdAt").GetDateTimeOffset();

        await store.UpdateAsync(card, CharacterVersionKind.Alternate, false, "改了", null);

        var after = JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        Assert.Equal(created, after.GetProperty("createdAt").GetDateTimeOffset());
        Assert.True(after.GetProperty("updatedAt").GetDateTimeOffset() >= created);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{ \"schemaVersion\": 99, \"cards\": {} }")]
    public async Task ADamagedDocumentCostsTheDirectoryItsAnnotationsButDoesNotThrow(string contents)
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png");
        var store = new CharacterAnnotationStore();
        var path = store.GetDocumentPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);

        Assert.Null(store.GetForCard(card));
    }

    // An unknown label must degrade one card, not the whole document — which
    // is why the label is stored as a string rather than a serialized enum.
    [Fact]
    public async Task AnUnknownLabelDegradesOneCardAndLeavesTheRestIntact()
    {
        using var directory = new TestDirectory();
        var store = new CharacterAnnotationStore();
        var path = store.GetDocumentPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            {
              "schemaVersion": 1,
              "cards": {
                "a.png": { "label": "from-the-future", "note": "保留" },
                "b.png": { "label": "alternate" }
              }
            }
            """);

        var first = store.GetForCard(Path.Combine(directory.Path, "a.png"));
        Assert.Equal(CharacterVersionKind.Current, first?.ParsedKind);
        Assert.Equal("保留", first?.Note);
        Assert.Equal(
            CharacterVersionKind.Alternate,
            store.GetForCard(Path.Combine(directory.Path, "b.png"))?.ParsedKind);
    }

    // Kind and superseded are independent, so a replaced what-if has to survive
    // the round trip as both facts.
    [Fact]
    public async Task AReplacedWhatIfRoundTripsAsBothFacts()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png");
        var store = new CharacterAnnotationStore();

        await store.UpdateAsync(card, CharacterVersionKind.Alternate, true, "舊的短髮 IF", null);

        var entry = store.GetForCard(card);
        Assert.Equal(CharacterVersionKind.Alternate, entry?.ParsedKind);
        Assert.True(entry?.IsSuperseded);
        Assert.Equal("舊的短髮 IF", entry?.Note);

        using var json = JsonDocument.Parse(
            File.ReadAllText(store.GetDocumentPath(directory.Path)));
        var stored = json.RootElement.GetProperty("cards").GetProperty("a.png");
        Assert.Equal("alternate", stored.GetProperty("label").GetString());
        Assert.True(stored.GetProperty("superseded").GetBoolean());
    }

    // Documents written before superseding became its own field used a third
    // label value. Those must keep meaning what they meant.
    [Fact]
    public async Task TheLegacyOldLabelStillReadsAsAReplacedCurrentCard()
    {
        using var directory = new TestDirectory();
        var store = new CharacterAnnotationStore();
        var path = store.GetDocumentPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            { "schemaVersion": 1, "cards": { "a.png": { "label": "old", "note": "留著" } } }
            """);

        var entry = store.GetForCard(Path.Combine(directory.Path, "a.png"));
        Assert.Equal(CharacterVersionKind.Current, entry?.ParsedKind);
        Assert.True(entry?.IsSuperseded);
        Assert.Equal("留著", entry?.Note);
        Assert.False(entry?.IsEmpty);
    }

    // Renaming a card outside the app orphans its entry. Keeping the entry is
    // what lets renaming it back restore the annotation.
    [Fact]
    public async Task AnEntryForAMissingFileIsKept()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png");
        var store = new CharacterAnnotationStore();
        await store.UpdateAsync(card, CharacterVersionKind.Alternate, false, "IF", null);

        File.Move(card, Path.Combine(directory.Path, "renamed.png"));
        store.ClearCache();

        Assert.Null(store.GetForCard(Path.Combine(directory.Path, "renamed.png")));
        Assert.Equal(CharacterVersionKind.Alternate, store.GetForCard(card)?.ParsedKind);
    }

    [Fact]
    public async Task ConcurrentUpdatesInOneDirectoryAllSurvive()
    {
        using var directory = new TestDirectory();
        var cards = Enumerable.Range(0, 12).Select(i => Card(directory, $"card{i}.png")).ToArray();
        var store = new CharacterAnnotationStore();

        await Task.WhenAll(cards.Select(card =>
            store.UpdateAsync(card, CharacterVersionKind.Current, true, Path.GetFileName(card), null)));

        store.ClearCache();
        foreach (var card in cards)
            Assert.Equal(Path.GetFileName(card), store.GetForCard(card)?.Note);
    }

    [Fact]
    public async Task ClearCacheMakesAnExternalEditVisible()
    {
        using var directory = new TestDirectory();
        var card = Card(directory, "a.png");
        var store = new CharacterAnnotationStore();
        Assert.Null(store.GetForCard(card));

        var path = store.GetDocumentPath(directory.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            """{ "schemaVersion": 1, "cards": { "a.png": { "label": "old" } } }""");

        Assert.Null(store.GetForCard(card));
        store.ClearCache();
        Assert.Equal(CharacterVersionKind.Current, store.GetForCard(card)?.ParsedKind);
    }
}

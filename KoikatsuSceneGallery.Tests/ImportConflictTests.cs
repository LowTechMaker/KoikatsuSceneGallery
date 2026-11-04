using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportConflictTests : IDisposable
{
    private readonly TestDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private string Card(string relative, string version)
        => _directory.Write(relative, TestFiles.BinaryCard("【KStudio】", version));

    [Fact]
    public void ADifferentCardOfTheSameNameIsAClashButTheSameCardIsNot()
    {
        var source = Card("in/a.png", "1");
        var different = Card("lib/a.png", "2");
        var same = Card("lib/b.png", "1");

        Assert.True(ImportConflictRules.IsNameClash(source, different));
        Assert.False(ImportConflictRules.IsNameClash(source, same));
        Assert.False(ImportConflictRules.IsNameClash(source, Path.Combine(_directory.Path, "lib/missing.png")));
    }

    [Fact]
    public void RenamingPicksTheFirstFreeSuffix()
    {
        var source = Card("in/a.png", "1");
        var destination = Card("lib/a.png", "2");
        Card("lib/a_1.png", "3");

        var renamed = ImportConflictRules.RenamedDestination(source, destination, new Dictionary<string, string>());

        Assert.Equal(Path.Combine(_directory.Path, "lib", "a_2.png"), renamed);
    }

    [Fact]
    public void ChoicesDependOnTheKindAndTheFirstIsSafe()
    {
        Assert.Equal([ImportConflictChoice.Rename, ImportConflictChoice.Overwrite, ImportConflictChoice.Skip],
            ImportConflictRules.ChoicesFor(ImportConflictKind.NameClash));
        Assert.Equal(ImportConflictChoice.Skip, ImportConflictRules.DefaultFor(ImportConflictKind.InLibraryElsewhere));
        Assert.Contains(ImportConflictChoice.HardLink, ImportConflictRules.ChoicesFor(ImportConflictKind.InLibraryElsewhere));
    }

    [Fact]
    public void AnOverwrittenFileCanBePutBack()
    {
        var existing = Card("lib/a.png", "old");
        var backup = new OverwriteBackup(Path.Combine(_directory.Path, "backup"));

        backup.Move(existing);
        Assert.False(File.Exists(existing));
        Assert.Empty(backup.Restore());
        Assert.True(File.Exists(existing));
    }

    [Fact]
    public void AHardLinkSharesTheLibrarysCopy()
    {
        var target = Card("lib/a.png", "1");
        var link = Path.Combine(_directory.Path, "lib", "author", "a.png");

        FileLinks.CreateHardLink(link, target);

        Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(link));
        File.AppendAllText(target, "x");
        Assert.Equal(File.ReadAllBytes(target), File.ReadAllBytes(link));
    }

    [Fact]
    public void AHardLinkOntoAnExistingFileFailsWithAReason()
    {
        var target = Card("lib/a.png", "1");
        var taken = Card("lib/b.png", "2");

        var error = Assert.Throws<IOException>(() => FileLinks.CreateHardLink(taken, target));
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }
}

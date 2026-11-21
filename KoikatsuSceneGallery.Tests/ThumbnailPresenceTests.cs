using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class ThumbnailPresenceTests
{
    [Fact]
    public void AThumbnailThatIsGoneIsMissingAndOneThatIsThereIsNot()
    {
        using var directory = new TestDirectory();
        var kept = directory.Write("thumbs/a.jpg", [1]);
        var gone = Path.Combine(directory.Path, "thumbs", "b.jpg");

        var result = ThumbnailPresence.Find([("a.png", kept), ("b.png", gone), ("c.png", null)]);

        Assert.Equal(["b.png"], result.Missing);
    }

    [Fact]
    public void EachFolderIsListedOnceHoweverManyThumbnailsItHolds()
    {
        var listed = new List<string>();
        IEnumerable<string>? Lister(string folder)
        {
            listed.Add(folder);
            return [Path.Combine(folder, "a.jpg"), Path.Combine(folder, "b.jpg")];
        }

        var result = ThumbnailPresence.Find(
            [("a.png", @"C:\t\a.jpg"), ("b.png", @"C:\t\b.jpg"), ("c.png", @"C:\t\c.jpg"), ("d.png", @"C:\old\d.jpg")],
            Lister);

        Assert.Equal([@"C:\t", @"C:\old"], listed);
        Assert.Equal(2, result.FoldersListed);
        Assert.Equal(["c.png", "d.png"], result.Missing.Order());
    }

    [Fact]
    public void AFolderThatCannotBeListedMakesItsThumbnailsMissing()
    {
        // Gone, renamed or unreadable: regenerating is the safe answer, as File.Exists said.
        var result = ThumbnailPresence.Find([("a.png", @"C:\gone\a.jpg")], _ => null);

        Assert.Equal(["a.png"], result.Missing);
    }

    [Fact]
    public void NamesCompareWithoutCase()
    {
        var result = ThumbnailPresence.Find([("a.png", @"C:\t\ABC.JPG")], folder => [Path.Combine(folder, "abc.jpg")]);

        Assert.Empty(result.Missing);
    }

    [Fact]
    public void NoThumbnailsListNothing()
    {
        var calls = 0;
        var result = ThumbnailPresence.Find([("a.png", null), ("b.png", "")], _ => { calls++; return []; });

        Assert.Equal(0, calls);
        Assert.Empty(result.Missing);
    }
}

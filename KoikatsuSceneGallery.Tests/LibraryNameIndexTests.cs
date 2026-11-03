using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class LibraryNameIndexTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("name-index").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, int bytes)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private LibraryNameIndex Scan() => LibraryNameIndex.Build([_root], (_, _, _) => { }, CancellationToken.None);

    [Fact]
    public void ANameAndSizeInAnyNestedFolderIsFound()
    {
        Write(Path.Combine("author", "post", "card.png"), 3);

        Assert.True(Scan().Has("card.png", 3));
        Assert.True(Scan().Has("CARD.PNG", 3));
    }

    [Fact]
    public void ADifferentSizeIsNotTheSameFileButNoSizeAcceptsAnyName()
    {
        Write("card.png", 3);

        Assert.False(Scan().Has("card.png", 4));
        Assert.True(Scan().Has("card.png", null));
    }

    [Fact]
    public void AWindowsCopySuffixOnEitherSideStillMatches()
    {
        Write("one (1).png", 5);
        Write("two.png", 6);
        var index = Scan();

        Assert.True(index.Has("one.png", 5));
        Assert.True(index.Has("two (1).png", 6));
    }

    [Fact]
    public void AnUnknownNameOrANonPngIsNotFound()
    {
        Write("card.jpg", 3);
        var index = Scan();

        Assert.False(index.Has("card.png", null));
        Assert.False(index.Has("", null));
    }

    [Fact]
    public void AMissingRootIsSkippedAndAFileGoneSinceTheScanIsNotFound()
    {
        var path = Write("card.png", 3);
        var index = LibraryNameIndex.Build([Path.Combine(_root, "nowhere"), _root], (_, _, _) => { }, CancellationToken.None);
        File.Delete(path);

        Assert.False(index.Has("card.png", 3));
    }
}

using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ScannedFileNameIndexTests
{
    [Fact]
    public void Match_ClaimsOnlyTheSingleUnownedLocalFile()
    {
        var index = new ScannedFileNameIndex();
        index.Record(@"C:\library\author\manual.png", null);
        index.Record(@"C:\library\author\12345\manual.png", "12345");

        var match = index.Match("99999", ["manual.png"]);

        Assert.Equal([@"C:\library\author\manual.png"], match.Files);
        Assert.False(match.HasAmbiguousName);
    }

    [Fact]
    public void Match_LeavesRepeatedUnownedNameUnclaimed()
    {
        var index = new ScannedFileNameIndex();
        index.Record(@"C:\library\author\a\manual.png", null);
        index.Record(@"C:\library\author\b\manual.png", null);

        var match = index.Match("99999", ["manual.png"]);

        Assert.Empty(match.Files);
        Assert.True(match.HasAmbiguousName);
    }
}

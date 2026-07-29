using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class JpegCacheFileTests
{
    [Fact]
    public void CompleteJpegMarkersAreAccepted()
    {
        using var directory = new TestDirectory();
        var path = directory.Write(
            "complete.jpg",
            [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0xFF, 0xD9]);

        Assert.True(JpegCacheFile.IsComplete(path));
    }

    [Fact]
    public void MissingEndMarkerIsRejected()
    {
        using var directory = new TestDirectory();
        var path = directory.Write(
            "truncated.jpg",
            [0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x00]);

        Assert.False(JpegCacheFile.IsComplete(path));
    }

    [Fact]
    public void EmptyAndMissingFilesAreRejected()
    {
        using var directory = new TestDirectory();
        var emptyPath = directory.Write("empty.jpg", []);

        Assert.False(JpegCacheFile.IsComplete(emptyPath));
        Assert.False(JpegCacheFile.IsComplete(
            Path.Combine(directory.Path, "missing.jpg")));
    }
}

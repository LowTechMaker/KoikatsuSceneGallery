using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class JpegCacheFileTests
{
    [Fact]
    public void IsComplete_RequiresBothJpegMarkers()
    {
        using var directory = new TestDirectory();
        var complete = directory.Write("complete.jpg", [0xFF, 0xD8, 0x01, 0x02, 0xFF, 0xD9]);
        var incomplete = directory.Write("incomplete.jpg", [0xFF, 0xD8, 0x01, 0x02]);

        Assert.True(JpegCacheFile.IsComplete(complete));
        Assert.False(JpegCacheFile.IsComplete(incomplete));
    }
}

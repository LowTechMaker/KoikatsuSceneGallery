using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class FileVersionCacheKeyTests
{
    [Theory]
    [InlineData(@"C:\Cards\a.png", 0L, "DD4B79A75CCE35A5")]
    [InlineData(@"C:\卡片\服裝.png", 638500000000000000L, "0E22FE8136BFBB73")]
    [InlineData(@"c:\cards\a.png", 0L, "049C0C197C7304BB")]
    public void PreservesExistingUtf8Sha256Prefix(string path, long ticks, string expected)
        => Assert.Equal(expected, FileVersionCacheKey.Compute(path, new DateTime(ticks)));

    [Fact]
    public void UsesTicksWithoutTimeZoneConversion()
    {
        const long ticks = 638500000000000000;
        var expected = FileVersionCacheKey.Compute("card.png", new DateTime(ticks, DateTimeKind.Unspecified));
        Assert.Equal(expected, FileVersionCacheKey.Compute("card.png", new DateTime(ticks, DateTimeKind.Utc)));
        Assert.Equal(expected, FileVersionCacheKey.Compute("card.png", new DateTime(ticks, DateTimeKind.Local)));
        Assert.NotEqual(expected, FileVersionCacheKey.Compute("card.png", new DateTime(ticks + 1)));
    }

    [Fact]
    public void DoesNotCanonicalizePaths()
    {
        var time = new DateTime(0);
        var expected = FileVersionCacheKey.Compute(@"C:\Cards\a.png", time);
        Assert.NotEqual(expected, FileVersionCacheKey.Compute("C:/Cards/a.png", time));
        Assert.NotEqual(expected, FileVersionCacheKey.Compute(@"C:\Cards\.\a.png", time));
    }
}

using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class AppFilesTests
{
    [Fact]
    public void EveryNameIsDistinctAndNotEmpty()
    {
        Assert.DoesNotContain(AppFiles.All, string.IsNullOrWhiteSpace);
        Assert.Equal(AppFiles.All.Count, AppFiles.All.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}

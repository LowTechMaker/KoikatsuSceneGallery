using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ArtworkPromotionServiceTests
{
    [Fact]
    public void Promote_MovesExistingRootFilesOnlyAfterPreflightSucceeds()
    {
        using var directory = new TestDirectory();
        var authorDirectory = Path.Combine(directory.Path, "作者");
        var artworkDirectory = Path.Combine(authorDirectory, "作品 (42)");
        var first = directory.Write(Path.Combine("作者", "pixiv_42_p0.png"), TestFiles.Png(320, 180));
        var second = directory.Write(Path.Combine("作者", "pixiv_42_p1.png"), TestFiles.Png(640, 360));
        var incoming = directory.Write(Path.Combine("incoming", "pixiv_42_p2.png"), TestFiles.Png(1280, 720));

        var result = ArtworkPromotionService.PreflightAndPromote(
            [first, second],
            [incoming],
            artworkDirectory);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(Path.Combine(artworkDirectory, Path.GetFileName(first))));
        Assert.True(File.Exists(Path.Combine(artworkDirectory, Path.GetFileName(second))));
        Assert.True(File.Exists(incoming));
    }

    [Fact]
    public void Promote_TargetCollisionLeavesEveryRootFileInPlace()
    {
        using var directory = new TestDirectory();
        var artworkDirectory = Path.Combine(directory.Path, "作者", "作品 (42)");
        var rootFile = directory.Write(Path.Combine("作者", "pixiv_42_p1.png"), TestFiles.Png(640, 360));
        var targetFile = directory.Write(
            Path.Combine("作者", "作品 (42)", "pixiv_42_p1.png"),
            TestFiles.Png(1920, 1080));

        var result = ArtworkPromotionService.PreflightAndPromote([rootFile], [], artworkDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal("pixiv_42_p1.png", result.CollisionFileName);
        Assert.True(File.Exists(rootFile));
        Assert.True(File.Exists(targetFile));
    }

    [Fact]
    public void Promote_DeletesOnlyAByteIdenticalRootDuplicate()
    {
        using var directory = new TestDirectory();
        var artworkDirectory = Path.Combine(directory.Path, "作者", "作品 (42)");
        var bytes = TestFiles.Png(320, 180);
        var rootFile = directory.Write(Path.Combine("作者", "pixiv_42.png"), bytes);
        var targetFile = directory.Write(Path.Combine("作者", "作品 (42)", "pixiv_42.png"), bytes);

        var result = ArtworkPromotionService.PreflightAndPromote([rootFile], [], artworkDirectory);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(rootFile));
        Assert.True(File.Exists(targetFile));
    }
}

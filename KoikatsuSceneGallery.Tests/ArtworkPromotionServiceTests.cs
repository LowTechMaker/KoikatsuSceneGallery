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
        var first = directory.Write(
            Path.Combine("作者", "pixiv_42_p0.png"),
            TestFiles.Png(320, 180));
        var second = directory.Write(
            Path.Combine("作者", "pixiv_42_p1.png"),
            TestFiles.Png(640, 360));
        var incoming = directory.Write(
            Path.Combine("incoming", "pixiv_42_p2.png"),
            TestFiles.Png(1280, 720));

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
        var safeRootFile = directory.Write(
            Path.Combine("作者", "pixiv_42_p0.png"),
            TestFiles.Png(320, 180));
        var collidingRootFile = directory.Write(
            Path.Combine("作者", "pixiv_42_p1.png"),
            TestFiles.Png(640, 360));
        var targetFile = directory.Write(
            Path.Combine("作者", "作品 (42)", "pixiv_42_p1.png"),
            TestFiles.Png(1920, 1080));

        var result = ArtworkPromotionService.PreflightAndPromote(
            [safeRootFile, collidingRootFile],
            [],
            artworkDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal("pixiv_42_p1.png", result.CollisionFileName);
        Assert.True(File.Exists(safeRootFile));
        Assert.True(File.Exists(collidingRootFile));
        Assert.True(File.Exists(targetFile));
        Assert.False(File.Exists(
            Path.Combine(artworkDirectory, Path.GetFileName(safeRootFile))));
    }

    [Fact]
    public void Promote_IncomingCollisionDoesNotCreateArtworkDirectory()
    {
        using var directory = new TestDirectory();
        var artworkDirectory = Path.Combine(directory.Path, "作者", "作品 (42)");
        var first = directory.Write(
            Path.Combine("incoming-a", "pixiv_42.png"),
            TestFiles.Png(320, 180));
        var second = directory.Write(
            Path.Combine("incoming-b", "pixiv_42.png"),
            TestFiles.Png(640, 360));

        var result = ArtworkPromotionService.PreflightAndPromote(
            [],
            [first, second],
            artworkDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal("pixiv_42.png", result.CollisionFileName);
        Assert.False(Directory.Exists(artworkDirectory));
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public void Promote_RemovesRootDuplicateWhenTargetHasIdenticalContent()
    {
        using var directory = new TestDirectory();
        var artworkDirectory = Path.Combine(directory.Path, "作者", "作品 (42)");
        var bytes = TestFiles.Png(320, 180);
        var rootFile = directory.Write(
            Path.Combine("作者", "pixiv_42.png"),
            bytes);
        var targetFile = directory.Write(
            Path.Combine("作者", "作品 (42)", "pixiv_42.png"),
            bytes);

        var result = ArtworkPromotionService.PreflightAndPromote(
            [rootFile],
            [],
            artworkDirectory);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(rootFile));
        Assert.True(File.Exists(targetFile));
    }
}

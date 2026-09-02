using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ArtworkFileAssignmentServiceTests
{
    [Fact]
    public void Move_CreatesArtworkFolderAndMovesEverySelectedFile()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = Path.Combine(directory.Path, "author");
        Directory.CreateDirectory(sourceDirectory);
        var first = Path.Combine(sourceDirectory, "a.png");
        var second = Path.Combine(sourceDirectory, "b.png");
        File.WriteAllText(first, "a");
        File.WriteAllText(second, "b");
        var destination = Path.Combine(sourceDirectory, "[SCENE] (12345)");

        new ArtworkFileAssignmentService().Move(
            [new(first, destination), new(second, destination)],
            CancellationToken.None);

        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.Equal("a", File.ReadAllText(Path.Combine(destination, "a.png")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(destination, "b.png")));
    }

    [Fact]
    public void Move_RejectsExistingDestinationWithoutMovingAnything()
    {
        using var directory = new TestDirectory();
        var sourceDirectory = Path.Combine(directory.Path, "author");
        var destination = Path.Combine(sourceDirectory, "[SCENE] (12345)");
        Directory.CreateDirectory(destination);
        var source = Path.Combine(sourceDirectory, "a.png");
        File.WriteAllText(source, "source");
        File.WriteAllText(Path.Combine(destination, "a.png"), "existing");

        Assert.Throws<IOException>(() => new ArtworkFileAssignmentService().Move(
            [new(source, destination)], CancellationToken.None));

        Assert.Equal("source", File.ReadAllText(source));
        Assert.Equal("existing", File.ReadAllText(Path.Combine(destination, "a.png")));
    }
}

using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// The folder name a rename produces. The trailing id is the only thing tying
/// a folder to its cards and its author identity, so every case here is about
/// that id surviving whatever the user types as a name.
/// </summary>
public sealed class LocalSourceRenameNameTests
{
    [Fact]
    public void TheIdIsKeptAndOnlyTheLabelChanges()
        => Assert.Equal(
            "夜羽 (local-k7f3q9)",
            LocalSourceIdentity.RenameFolderName("阿明 (local-k7f3q9)", "夜羽"));

    // Whatever the new name comes back as, it has to parse back to the same id.
    [Theory]
    [InlineData("夜羽")]
    [InlineData("a/b")]
    [InlineData("  spaced  ")]
    [InlineData("dots...")]
    [InlineData("(parens)")]
    [InlineData("///")]
    [InlineData("名字 (local-other)")]
    public void TheResultAlwaysParsesBackToTheSameId(string newName)
    {
        var renamed = LocalSourceIdentity.RenameFolderName("阿明 (local-k7f3q9)", newName);

        Assert.NotNull(renamed);
        Assert.True(LocalSourceIdentity.TryParseFolderId(renamed, out var id, out _));
        Assert.Equal("local-k7f3q9", id);
    }

    [Fact]
    public void PathSeparatorsInTheNameCannotEscapeTheFolder()
    {
        var renamed = LocalSourceIdentity.RenameFolderName(
            "阿明 (local-k7f3q9)",
            @"..\..\elsewhere");

        Assert.NotNull(renamed);
        Assert.Equal(renamed, Path.GetFileName(renamed));
    }

    // Null means "leave the folder alone", which is what keeps a rename from
    // producing a folder whose label is gone entirely.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ANameThatSanitizesToNothingLeavesTheFolderAlone(string newName)
        => Assert.Null(LocalSourceIdentity.RenameFolderName("阿明 (local-k7f3q9)", newName));

    [Fact]
    public void RenamingToTheCurrentNameLeavesTheFolderAlone()
        => Assert.Null(LocalSourceIdentity.RenameFolderName("阿明 (local-k7f3q9)", "阿明"));

    [Fact]
    public void RenamingToTheCurrentNameWithStraySpaceStillLeavesItAlone()
        => Assert.Null(LocalSourceIdentity.RenameFolderName("阿明 (local-k7f3q9)", "  阿明  "));

    [Theory]
    [InlineData("no id here")]
    [InlineData("阿明 (12345)")]
    [InlineData("")]
    [InlineData(null)]
    public void AFolderThatIsNotALocalSourceIsNeverRenamed(string? folderName)
        => Assert.Null(LocalSourceIdentity.RenameFolderName(folderName, "夜羽"));
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportDestinationPolicyTests
{
    private static readonly ImportPathOptions Options = new(
        Path.Combine("整理", "pixiv"),
        "{name} ({id})",
        "{title} ({id})");

    [Fact]
    public void DestinationPath_PreservesMultiLevelUnicodeLayout()
    {
        var author = ImportDestinationPolicy.FormatAuthorFolder(Options, "作者_日本語", "42");
        var path = ImportDestinationPolicy.BuildTargetBase(
            "library", Options.ImportSubfolder, "Pixiv", "Koikatsu", "R-18", author);

        Assert.Equal(
            Path.Combine("library", "整理", "pixiv", "Pixiv", "Koikatsu", "R-18", "作者_日本語 (42)"),
            path);
    }

    [Fact]
    public void DestinationPath_EmptySubfolderKeepsLegacyDirectLayout()
        => Assert.Equal(
            Path.Combine("library", "provider", "author"),
            ImportDestinationPolicy.BuildTargetBase("library", "", "provider", "", "", "author"));

    [Theory]
    [InlineData(false, 1, 1, null, false)]
    [InlineData(false, 1, 2, null, true)]
    [InlineData(false, 0, 1, null, true)]
    [InlineData(false, -1, 99, null, false)]
    [InlineData(false, -1, 1, true, true)]
    [InlineData(false, 0, 99, false, false)]
    [InlineData(true, -1, 0, false, true)]
    public void ArtworkFolder_UsesStrictThresholdAndVisualOverride(
        bool exists, int threshold, int count, bool? visual, bool expected)
        => Assert.Equal(
            expected,
            ImportDestinationPolicy.ShouldCreateArtworkFolder(exists, threshold, count, visual));

    [Fact]
    public void ArtworkFolder_UsesTitleAndFallsBackToId()
    {
        Assert.Equal("作品 (123)", ImportDestinationPolicy.FormatArtworkFolder(Options, "作品", "123"));
        Assert.Equal("(123)", ImportDestinationPolicy.FormatArtworkFolder(Options, null, "123"));
    }

    [Fact]
    public void DuplicateFilename_IsCaseInsensitiveWhenLibraryIndexIsCaseInsensitive()
    {
        IReadOnlySet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "同名.PNG",
        };

        Assert.True(ImportDestinationPolicy.IsDuplicateFilename(existing, "同名.png"));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, true, 1)]
    [InlineData(true, false, 2)]
    public void FileConflict_PreservesMoveDeleteOrSkipBehavior(
        bool destinationExists,
        bool identical,
        int expected)
        => Assert.Equal(
            (ImportFileConflict)expected,
            ImportDestinationPolicy.ClassifyFileConflict(destinationExists, identical));

    [Theory]
    [InlineData(CardType.Scene, "scene")]
    [InlineData(CardType.Character, "character")]
    [InlineData(CardType.Coordinate, "coordinate")]
    public void CardTypeRouting_SelectsTheExistingLibraryRoot(CardType cardType, string expected)
        => Assert.Equal(
            expected,
            Assert.Single(ImportDestinationPolicy.SelectRoots(
                cardType,
                ["scene"],
                ["character"],
                ["coordinate"])));

    [Fact]
    public void DuplicateDetector_RecognizesSyntheticCardCopiesAcrossDirectories()
    {
        using var directory = new TestDirectory();
        var bytes = TestFiles.Png(320, 180);
        var source = directory.Write(Path.Combine("來源", "同名.png"), bytes);
        var destination = directory.Write(Path.Combine("收藏庫", "同名.png"), bytes);

        Assert.True(ImportDuplicateDetector.AreFilesIdentical(source, destination));
    }

    [Fact]
    public void DuplicateDetector_RejectsDifferentSyntheticCards()
    {
        using var directory = new TestDirectory();
        var source = directory.Write("source.png", TestFiles.Png(320, 180));
        var destination = directory.Write("destination.png", TestFiles.Png(1600, 900));

        Assert.False(ImportDuplicateDetector.AreFilesIdentical(source, destination));
    }

    [Fact]
    public void DuplicateDetector_PropagatesCancellation()
    {
        using var directory = new TestDirectory();
        var bytes = TestFiles.Png(320, 180);
        var source = directory.Write("source.png", bytes);
        var destination = directory.Write("destination.png", bytes);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ImportDuplicateDetector.AreFilesIdentical(source, destination, cts.Token));
    }
}

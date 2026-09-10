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
    public async Task LibraryFileCache_ReusesIndexAndRegistersCommittedFiles()
    {
        using var directory = new TestDirectory();
        var libraryRoot = Path.Combine(directory.Path, "Library");
        var existing = Path.Combine(libraryRoot, "existing.png");
        var committed = Path.Combine(libraryRoot, "nested", "committed.png");
        var buildCount = 0;
        var cache = new LibraryFileCache();

        Dictionary<string, List<string>> BuildIndex(CancellationToken _)
        {
            buildCount++;
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["existing.png"] = [existing],
            };
        }

        var first = await cache.GetOrBuildAsync([libraryRoot], BuildIndex, CancellationToken.None);
        var second = await cache.GetOrBuildAsync([libraryRoot], BuildIndex, CancellationToken.None);

        Assert.True(first.WasRebuilt);
        Assert.False(second.WasRebuilt);
        Assert.Equal(1, buildCount);

        cache.RegisterCommittedFiles([committed]);
        var afterCommit = await cache.GetOrBuildAsync([libraryRoot], BuildIndex, CancellationToken.None);

        Assert.False(afterCommit.WasRebuilt);
        Assert.Equal(1, buildCount);
        Assert.Contains(committed, afterCommit.FilesByName["committed.png"]);
    }

    [Fact]
    public async Task LibraryFileCache_RebuildsWhenRootsChangeOrItIsInvalidated()
    {
        using var directory = new TestDirectory();
        var firstRoot = Path.Combine(directory.Path, "First");
        var secondRoot = Path.Combine(directory.Path, "Second");
        var buildCount = 0;
        var cache = new LibraryFileCache();

        Dictionary<string, List<string>> BuildIndex(CancellationToken _)
        {
            buildCount++;
            return [];
        }

        await cache.GetOrBuildAsync([firstRoot], BuildIndex, CancellationToken.None);
        var rootsChanged = await cache.GetOrBuildAsync([secondRoot], BuildIndex, CancellationToken.None);
        cache.Invalidate();
        var invalidated = await cache.GetOrBuildAsync([secondRoot], BuildIndex, CancellationToken.None);

        Assert.True(rootsChanged.WasRebuilt);
        Assert.True(invalidated.WasRebuilt);
        Assert.Equal(3, buildCount);
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
    public void DuplicateDetector_RejectsSameLengthFilesWithDifferentContent()
    {
        using var directory = new TestDirectory();
        var sourceBytes = new byte[20 * 1024];
        sourceBytes[0] = 1;
        sourceBytes[^1] = 2;
        var destinationBytes = (byte[])sourceBytes.Clone();
        destinationBytes[^1] = 3;

        var source = directory.Write("source.png", sourceBytes);
        var destination = directory.Write("destination.png", destinationBytes);

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

    [Theory]
    // The sink exists for files that have an author but no artwork identity.
    [InlineData(false, true, "pixiv", true)]
    [InlineData(false, true, null, true)]
    // An artwork identity gets its own folder instead.
    [InlineData(true, true, "pixiv", false)]
    // Without an author there is no author folder to sink into.
    [InlineData(false, false, "pixiv", false)]
    [InlineData(true, false, "pixiv", false)]
    // Local sources never have an artwork identity, so they must be exempt or
    // every local card would land in the sink and lose its folder grouping.
    [InlineData(false, true, "local", false)]
    [InlineData(false, true, "LOCAL", false)]
    public void UsesUnrecognizedSink_ExemptsLocalSources(
        bool hasArtwork,
        bool hasAuthor,
        string? authorProviderId,
        bool expected)
        => Assert.Equal(
            expected,
            ImportDestinationPolicy.UsesUnrecognizedSink(hasArtwork, hasAuthor, authorProviderId));
}

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class FilenameAndPathTests
{
    [Fact]
    public void ParseTimestamp_ParsesEmbeddedTimestamp()
    {
        var parsed = CharacterCardFilenameParser.ParseTimestamp("prefix_Koikatu_F_20260102030405123_角色.png");

        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, 123), parsed);
    }

    [Theory]
    [InlineData("koikatu_F_20260102030405123_name.png")]
    [InlineData("Koikatu_F_20261302030405123_name.png")]
    [InlineData("Koikatu_X_20260102030405123_name.png")]
    public void ParseTimestamp_UnrecognizedShapeReturnsNull(string fileName)
        => Assert.Null(CharacterCardFilenameParser.ParseTimestamp(fileName));

    [Fact]
    public void FilenameLinkParser_ExtractsPixivFromUnicodePath()
    {
        var result = FilenameLinkParser.Parse(Path.Combine("作者_日本語", "作品_12345678_p2.png"));

        Assert.Equal("12345678", result.PixivArtworkId);
        Assert.Equal("https://www.pixiv.net/artworks/12345678", result.PixivUrl);
        Assert.Null(result.BepisDbId);
    }

    [Theory]
    [InlineData("KKSCENE_42.png", "KKSCENE_42", "https://db.bepis.moe/kkscenes/view/42")]
    [InlineData("KKCLOTHING_99.png", "KKCLOTHING_99", "https://db.bepis.moe/kkclothing/view/99")]
    [InlineData("KK_7.png", "KK_7", "https://db.bepis.moe/koikatsu/view/7")]
    public void FilenameLinkParser_ExtractsBepisLinks(string fileName, string id, string url)
    {
        var result = FilenameLinkParser.Parse(fileName);

        Assert.Equal(id, result.BepisDbId);
        Assert.Equal(url, result.BepisDbUrl);
    }

    [Fact]
    public void FilenameLinkParser_SuppressesPixivMatchOverlappingBepisId()
    {
        var result = FilenameLinkParser.Parse("KK_123456_p0.png");

        Assert.Equal("KK_123456", result.BepisDbId);
        Assert.Null(result.PixivArtworkId);
    }

    [Fact]
    public void FilenameLinkParser_IsCaseSensitive()
    {
        var result = FilenameLinkParser.Parse("kkscene_42_123456_p0.png");

        Assert.Null(result.BepisDbId);
        Assert.Equal("123456", result.PixivArtworkId);
    }

    [Fact]
    public void FilenameLinkParser_NullReturnsSharedEmptyValue()
        => Assert.Same(FilenameLinkParser.Empty, FilenameLinkParser.Parse(null));

    [Fact]
    public void SanitizeFolderName_ReplacesPlatformInvalidCharactersAndTrims()
    {
        char invalid = Path.GetInvalidFileNameChars()[0];

        Assert.Equal("前_後", PathSanitizer.SanitizeFolderName($"  前{invalid}後  "));
    }

    [Fact]
    public void SanitizeRelativePath_RemovesEmptySegmentsAndPreservesUnicode()
    {
        char separator = Path.DirectorySeparatorChar;
        string input = $"{separator}中文{separator}{separator}日本語{separator}";

        Assert.Equal(Path.Combine("中文", "日本語"), PathSanitizer.SanitizeRelativePath(input));
    }

    [Fact]
    public void SanitizeRelativePath_LongSegmentAndDuplicateInputRemainDeterministic()
    {
        string input = new('長', 220);

        Assert.Equal(input, PathSanitizer.SanitizeRelativePath(input));
        Assert.Equal(
            PathSanitizer.SanitizeRelativePath("duplicate"),
            PathSanitizer.SanitizeRelativePath("duplicate"));
    }

    [Fact]
    public void PngHelper_HandlesLongUnicodePathsAndDuplicateFileNames()
    {
        using var directory = new TestDirectory();
        string longFolder = Path.Combine(new string('長', 60), new string('路', 60));
        var first = directory.Write(Path.Combine(longFolder, "同名.png"), TestFiles.Png(10, 20));
        var second = directory.Write(Path.Combine("別資料夾", "同名.png"), TestFiles.Png(30, 40));

        Assert.Equal((10, 20), PngHelper.ReadDimensions(first));
        Assert.Equal((30, 40), PngHelper.ReadDimensions(second));
    }
}

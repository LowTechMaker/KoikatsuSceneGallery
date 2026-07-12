using System.Text;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardTypeAndSceneParserTests
{
    [Theory]
    [InlineData("KoiKatuChara", CardType.Character, GameVersion.Koikatsu)]
    [InlineData("KoiKatuCharaSun", CardType.Character, GameVersion.KoikatsuSunshine)]
    [InlineData("KoiKatuClothes", CardType.Coordinate, GameVersion.Koikatsu)]
    [InlineData("KoiKatuClothesSun", CardType.Coordinate, GameVersion.KoikatsuSunshine)]
    public void CardTypeClassifier_ClassifiesBinaryCardMarkers(string marker, CardType type, GameVersion game)
    {
        using var directory = new TestDirectory();
        var path = directory.Write("カード.png", TestFiles.BinaryCard(marker, "1.0"));

        Assert.Equal((type, game), CardTypeClassifier.ClassifyExtended(path));
        Assert.Equal(type, CardTypeClassifier.Classify(path));
    }

    [Fact]
    public void CardTypeClassifier_ClassifiesKoikatsuSceneWithoutSunshineMarker()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("scene.png", TestFiles.Png(appended: Encoding.UTF8.GetBytes("prefix【KStudio】suffix")));

        Assert.Equal((CardType.Scene, GameVersion.Koikatsu), CardTypeClassifier.ClassifyExtended(path));
    }

    [Fact]
    public void CardTypeClassifier_ClassifiesSunshineSceneWhenBothTokensExist()
    {
        using var directory = new TestDirectory();
        var appended = Encoding.UTF8.GetBytes("KoiKatuCharaSun...【KStudio】");
        var path = directory.Write("scene-kks.png", TestFiles.Png(appended: appended));

        Assert.Equal((CardType.Scene, GameVersion.KoikatsuSunshine), CardTypeClassifier.ClassifyExtended(path));
    }

    [Fact]
    public void CardTypeClassifier_PlainPngAndMissingFileAreNotCards()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("plain.png", TestFiles.Png());

        Assert.Equal((CardType.NotACard, GameVersion.Unknown), CardTypeClassifier.ClassifyExtended(path));
        Assert.Equal(CardType.NotACard, CardTypeClassifier.Classify(Path.Combine(directory.Path, "missing.png")));
    }

    [Theory]
    [InlineData("【KStudio】", GameVersion.Unknown)]
    [InlineData("KoiKatuChara...【KStudio】", GameVersion.Koikatsu)]
    [InlineData("KoiKatuCharaSun...【KStudio】", GameVersion.KoikatsuSunshine)]
    public void SceneMetadataParser_PreservesCurrentGameDetection(string appendedText, GameVersion expected)
    {
        using var directory = new TestDirectory();
        var path = directory.Write("場景.png", TestFiles.Png(appended: Encoding.UTF8.GetBytes(appendedText)));

        Assert.Equal(new ParsedScene(expected), SceneMetadataParser.TryParse(path));
    }

    [Fact]
    public void SceneMetadataParser_FindsStudioTokenAcrossReadBoundary()
    {
        const int chunkSize = 1 << 20;
        byte[] token = Encoding.UTF8.GetBytes("【KStudio】");
        byte[] appended = new byte[chunkSize + token.Length];
        token.CopyTo(appended, chunkSize - 2);
        using var directory = new TestDirectory();
        var path = directory.Write("boundary.png", TestFiles.Png(appended: appended));

        Assert.Equal(new ParsedScene(GameVersion.Unknown), SceneMetadataParser.TryParse(path));
    }

    [Fact]
    public void SceneMetadataParser_InvalidInputReturnsNull()
    {
        using var directory = new TestDirectory();
        var plain = directory.Write("plain.png", TestFiles.Png());

        Assert.Null(SceneMetadataParser.TryParse(plain));
        Assert.Null(SceneMetadataParser.TryParse(Path.Combine(directory.Path, "missing.png")));
    }

    [Fact]
    public void SceneClassifier_MapsNullAndParsedValues()
    {
        Assert.Same(SceneMetadata.Unknown, SceneClassifier.Classify(null));
        Assert.Equal(new SceneMetadata(GameVersion.KoikatsuSunshine), SceneClassifier.Classify(new ParsedScene(GameVersion.KoikatsuSunshine)));
    }
}

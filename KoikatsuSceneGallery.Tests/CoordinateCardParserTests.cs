using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CoordinateCardParserTests
{
    [Fact]
    public void TryParse_ReturnsCoordinateName()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("座標.png", TestFiles.BinaryCard("KoiKatuClothesSun", "1.0", "制服"));

        Assert.Equal(new CoordinateMetadata("制服"), CoordinateCardParser.TryParse(path));
    }

    [Fact]
    public void TryParse_EmptyCoordinateNameBecomesNull()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("empty.png", TestFiles.BinaryCard("prefix-KoiKatuClothes-suffix", "1.0", string.Empty));

        Assert.Equal(CoordinateMetadata.Unknown, CoordinateCardParser.TryParse(path));
    }

    [Fact]
    public void TryParse_InvalidMarkerTruncatedDataAndMissingFileReturnNull()
    {
        using var directory = new TestDirectory();
        var invalidMarker = directory.Write("character.png", TestFiles.BinaryCard("KoiKatuChara", "1.0", "name"));
        var truncated = directory.Write("truncated.png", TestFiles.Png(appended: [1, 2, 3]));

        Assert.Null(CoordinateCardParser.TryParse(invalidMarker));
        Assert.Null(CoordinateCardParser.TryParse(truncated));
        Assert.Null(CoordinateCardParser.TryParse(Path.Combine(directory.Path, "missing.png")));
    }
}

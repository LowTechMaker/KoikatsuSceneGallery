using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CharacterCardParserTests
{
    [Fact]
    public void TryParse_ReadsParameterBlockAndMadevilPlugin()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("角色.png", TestFiles.CharacterCard());

        var result = CharacterCardParser.TryParse(path);

        Assert.Equal(new CharacterMetadata("姓", "名", "暱稱", 1, GameVersion.Koikatsu, true), result);
    }

    [Fact]
    public void TryParse_DetectsSunshineAndCaseInsensitiveMadevilPrefix()
    {
        using var directory = new TestDirectory();
        var path = directory.Write(
            "sunshine.png",
            TestFiles.CharacterCard(marker: "KoiKatuCharaSun", pluginKey: "MADEVIL.Plugin"));

        var result = CharacterCardParser.TryParse(path);

        Assert.NotNull(result);
        Assert.Equal(GameVersion.KoikatsuSunshine, result.Game);
        Assert.True(result.IsMadevil);
    }

    [Fact]
    public void TryParse_NonMadevilPluginLeavesFlagFalse()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("normal.png", TestFiles.CharacterCard(pluginKey: "com.example.plugin"));

        Assert.False(CharacterCardParser.TryParse(path)!.IsMadevil);
    }

    [Fact]
    public void TryParse_InvalidMarkerTruncatedDataAndMissingFileReturnNull()
    {
        using var directory = new TestDirectory();
        var invalidMarker = directory.Write("invalid.png", TestFiles.CharacterCard(marker: "UnknownChara"));
        var truncated = directory.Write("truncated.png", TestFiles.Png(appended: [1, 2, 3]));

        Assert.Null(CharacterCardParser.TryParse(invalidMarker));
        Assert.Null(CharacterCardParser.TryParse(truncated));
        Assert.Null(CharacterCardParser.TryParse(Path.Combine(directory.Path, "missing.png")));
    }
}

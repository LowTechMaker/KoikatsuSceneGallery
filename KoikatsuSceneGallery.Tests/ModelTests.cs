using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

public sealed class ModelTests
{
    [Theory]
    [InlineData(GameVersion.Unknown, false, CardSource.Unknown)]
    [InlineData(GameVersion.Koikatsu, false, CardSource.KoikatsuHF)]
    [InlineData(GameVersion.KoikatsuSunshine, false, CardSource.KoikatsuSunshine)]
    [InlineData(GameVersion.Unknown, true, CardSource.Madevil)]
    [InlineData(GameVersion.KoikatsuSunshine, true, CardSource.Madevil)]
    public void CardSourceClassifier_PreservesCurrentPrecedence(GameVersion game, bool madevil, CardSource expected)
        => Assert.Equal(expected, CardSourceClassifier.Classify(game, madevil));

    [Fact]
    public void CharacterMetadata_ComputesFullNameAndSource()
    {
        var metadata = new CharacterMetadata("山田", "花子", "はな", CharacterMetadata.SexFemale, GameVersion.Koikatsu, true);

        Assert.Equal("山田 花子", metadata.FullName);
        Assert.Equal(CardSource.Madevil, metadata.Source);
    }

    [Fact]
    public void CharacterMetadata_FullNameDropsWhitespaceOnlyComponents()
    {
        var metadata = new CharacterMetadata(" ", "名前", null, -1, GameVersion.Unknown, false);

        Assert.Equal("名前", metadata.FullName);
        Assert.Equal(CharacterMetadata.Unknown, new CharacterMetadata(null, null, null, -1, GameVersion.Unknown, false));
    }

    [Fact]
    public void CoordinateAndSceneUnknownSentinelsKeepNullAndUnknownValues()
    {
        Assert.Null(CoordinateMetadata.Unknown.CoordinateName);
        Assert.Equal(GameVersion.Unknown, SceneMetadata.Unknown.Game);
    }

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" -1 x 0 ", -1, 0)]
    public void ResolutionOption_TryParseAcceptsCurrentNumericForms(string input, int width, int height)
    {
        var parsed = ResolutionOption.TryParse(input);

        Assert.Equal(new ResolutionOption(width, height), parsed);
        Assert.Equal($"{width}x{height}", parsed!.ToString());
    }

    [Theory]
    [InlineData("1920X1080")]
    [InlineData("1920x1080x60")]
    [InlineData("")]
    public void ResolutionOption_TryParseRejectsOtherShapes(string input)
        => Assert.Null(ResolutionOption.TryParse(input));
}

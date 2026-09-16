using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public class ImportSourceSelectionTests
{
    [Fact]
    public void AmbiguousNumericIdRequiresExplicitSource()
    {
        Assert.Null(ImportSourceSelection.Resolve(null, ["pixiv", "bepis"], ["pixiv", "bepis"]));
        Assert.Equal("bepis", ImportSourceSelection.Resolve("bepis", ["pixiv", "bepis"], ["pixiv", "bepis"]));
    }

    [Fact]
    public void RecognizableIdSelectsItsOnlyMatchingSource()
        => Assert.Equal("pixiv", ImportSourceSelection.Resolve(null, ["pixiv", "bepis"], ["pixiv"]));

    [Fact]
    public void NoMatchingSourceDoesNotGuessFromPluginOrder()
        => Assert.Null(ImportSourceSelection.Resolve(null, ["pixiv", "bepis"], []));

    [Fact]
    public void MissingPluginsDoNotCrashOrFallBackToAnotherSource()
    {
        Assert.Null(ImportSourceSelection.Resolve(null, [], []));
        Assert.Null(ImportSourceSelection.Resolve("bepis", ["pixiv"], ["pixiv"]));
    }

    [Fact]
    public void SingleInstalledSourceAcceptsPlainId()
        => Assert.Equal("pixiv", ImportSourceSelection.Resolve(null, ["pixiv"], []));
}

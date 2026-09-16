using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class BrowseSequenceTests
{
    [Fact] public void RemovalKeepsSourceOrderAcrossTypesAndSkipsDeletedMembers()
    {
        string[] original = ["scene/a", "chara/b", "coord/c", "scene/d"];
        string[] live = ["scene/d", "scene/a"];
        Assert.Equal("scene/d", BrowseSequence.AfterRemoval(original, live, "CHARA/B", x => x));
    }
    [Fact] public void LastImageFallsBackToPreviousSurvivor()
        => Assert.Equal("a", BrowseSequence.AfterRemoval(new[] { "a", "b", "c" }, new[] { "a" }, "c", x => x));
    [Fact] public void EmptyScopeDoesNotEscapeToAnotherImage()
        => Assert.Null(BrowseSequence.AfterRemoval(new[] { "a" }, Array.Empty<string>(), "a", x => x));
}

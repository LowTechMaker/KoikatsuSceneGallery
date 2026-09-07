using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class GallerySearchTests
{
    [Theory]
    [InlineData("C:/scenes/beach.png", "Alice", "ALICE", "beach", true)]
    [InlineData("C:/scenes/beach.png", "Alice", "Alice", "winter", false)]
    [InlineData("C:/scenes/beach.png", null, "SCENES", "beach", true)]
    [InlineData("C:/scenes/beach.png", null, "Alice", "beach", false)]
    public void KeywordsCanMatchDifferentFieldsButAllMustMatch(
        string path, string? author, string first, string second, bool expected)
        => Assert.Equal(expected, GallerySearch.Matches(path, author, [first, second]));

    [Fact]
    public void NoKeywordsMatchesEverything()
        => Assert.True(GallerySearch.Matches("C:/scenes/card.png", null, []));
}

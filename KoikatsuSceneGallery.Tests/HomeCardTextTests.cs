using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class HomeCardTextTests
{
    [Fact]
    public void ThePostTitleWins()
        => Assert.Equal("夏の海", HomeCardText.Title("夏の海", "136615689_p8.png", "Other (123)", "123", "42"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPostTitleIsNoTitle(string? title)
        => Assert.Equal("136615689_p8", HomeCardText.Title(title, "136615689_p8.png", null, null, null));

    [Fact]
    public void AnArtworkFolderNamesTheTitleWhenThereIsNoSidecar()
        => Assert.Equal("Summer Beach", HomeCardText.Title(null, "a.png", "Summer Beach (123)", "123", "42"));

    [Fact]
    public void TheAuthorsOwnFolderIsNotATitle()
        // "Artist (42)" parses as an artwork folder too; it is the author's.
        => Assert.Equal("a", HomeCardText.Title(null, "a.png", "Artist (42)", "42", "42"));

    [Fact]
    public void AFolderThatIsOnlyTheIdIsNotATitle()
        => Assert.Equal("a", HomeCardText.Title(null, "a.png", "(123)", "123", "42"));

    [Fact]
    public void AFolderWhoseIdIsElsewhereInTheNameIsNotATitle()
        => Assert.Equal("a", HomeCardText.Title(null, "a.png", "Something else", "123", "42"));

    [Fact]
    public void TagsPreferTheTranslationAndAreAHandful()
    {
        var tags = HomeCardText.Tags(
        [
            ("コイカツ!", "Koikatsu!"),
            ("水着", null),
            ("koikatsu!", null),
            ("", "  "),
            ("海", "sea"),
            ("夏", "summer"),
            ("女の子", "girl"),
            ("制服", "uniform"),
        ], 5);

        Assert.Equal(["Koikatsu!", "水着", "sea", "summer", "girl"], tags);
    }

    [Fact]
    public void NoTagsGiveNone()
        => Assert.Empty(HomeCardText.Tags([], 5));
}

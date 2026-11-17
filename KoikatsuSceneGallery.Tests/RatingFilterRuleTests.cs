using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class RatingFilterRuleTests
{
    public static TheoryData<RatingFilterOption, int, bool> Matrix => new()
    {
        { RatingFilterOption.All, SceneRating.General, true },
        { RatingFilterOption.All, SceneRating.R18, true },
        { RatingFilterOption.All, SceneRating.R18G, true },
        { RatingFilterOption.GeneralOnly, SceneRating.General, true },
        { RatingFilterOption.GeneralOnly, SceneRating.R18, false },
        { RatingFilterOption.GeneralOnly, SceneRating.R18G, false },
        { RatingFilterOption.R18AndR18G, SceneRating.General, false },
        { RatingFilterOption.R18AndR18G, SceneRating.R18, true },
        { RatingFilterOption.R18AndR18G, SceneRating.R18G, true },
        { RatingFilterOption.R18Only, SceneRating.General, false },
        { RatingFilterOption.R18Only, SceneRating.R18, true },
        { RatingFilterOption.R18Only, SceneRating.R18G, false },
        { RatingFilterOption.R18GOnly, SceneRating.General, false },
        { RatingFilterOption.R18GOnly, SceneRating.R18, false },
        { RatingFilterOption.R18GOnly, SceneRating.R18G, true },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ACardPassesWhenItsRatingIsOneTheChoiceShows(RatingFilterOption choice, int rating, bool passes)
        => Assert.Equal(passes, RatingFilterRule.Passes(choice, rating));

    [Fact]
    public void TheChoicesKeepTheOrderTheMenusBindBy()
        // The title bar menu and the stored value go by these numbers; a reorder would
        // silently swap what a saved choice means.
        => Assert.Equal([0, 1, 2, 3, 4], Enum.GetValues<RatingFilterOption>().Select(o => (int)o));

    [Theory]
    [InlineData(RatingFilterOption.GeneralOnly)]
    [InlineData(RatingFilterOption.R18AndR18G)]
    [InlineData(RatingFilterOption.R18Only)]
    [InlineData(RatingFilterOption.R18GOnly)]
    public void WithFilteringSwitchedOffEveryCardPassesAndTheChoiceIsKept(RatingFilterOption choice)
    {
        Assert.Equal(RatingFilterOption.All, RatingFilterRule.Effective(enabled: false, choice));
        Assert.Equal(choice, RatingFilterRule.Effective(enabled: true, choice));
        foreach (var rating in new[] { SceneRating.General, SceneRating.R18, SceneRating.R18G })
            Assert.True(RatingFilterRule.Passes(RatingFilterRule.Effective(false, choice), rating));
    }

    [Theory]
    [InlineData(RatingFilterOption.All)]
    [InlineData(RatingFilterOption.GeneralOnly)]
    [InlineData(RatingFilterOption.R18AndR18G)]
    [InlineData(RatingFilterOption.R18Only)]
    [InlineData(RatingFilterOption.R18GOnly)]
    public void AStoredChoiceReadsBackTheSame(RatingFilterOption choice)
        => Assert.Equal(choice, RatingFilterRule.Parse(RatingFilterRule.ToConfigValue(choice)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Nonsense")]
    [InlineData("7")]
    [InlineData("-1")]
    public void AMissingOrUnknownStoredChoiceIsAll(string? stored)
        => Assert.Equal(RatingFilterOption.All, RatingFilterRule.Parse(stored));

    [Fact]
    public void AStoredChoiceIsReadWhateverItsCase()
        => Assert.Equal(RatingFilterOption.GeneralOnly, RatingFilterRule.Parse("generalonly"));
}

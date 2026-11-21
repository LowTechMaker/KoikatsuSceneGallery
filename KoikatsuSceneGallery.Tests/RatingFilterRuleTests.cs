using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class RatingFilterRuleTests
{
    private const RatingSet G = RatingSet.General, R = RatingSet.R18, RG = RatingSet.R18G;

    // ------------------------------------------------------------ does a card pass

    public static TheoryData<RatingSet, int, bool> Matrix()
    {
        var data = new TheoryData<RatingSet, int, bool>();
        // Every non-empty set, every rating: a card passes when its rating is in the set.
        for (var set = 1; set <= 7; set++)
        {
            data.Add((RatingSet)set, SceneRating.General, (set & 1) != 0);
            data.Add((RatingSet)set, SceneRating.R18, (set & 2) != 0);
            data.Add((RatingSet)set, SceneRating.R18G, (set & 4) != 0);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ACardPassesWhenItsRatingIsInTheSet(RatingSet set, int rating, bool passes)
        => Assert.Equal(passes, RatingFilterRule.Passes(set, rating));

    [Fact]
    public void GAndR18TogetherIsAChoiceLikeAnyOther()
    {
        Assert.True(RatingFilterRule.Passes(G | R, SceneRating.General));
        Assert.True(RatingFilterRule.Passes(G | R, SceneRating.R18));
        Assert.False(RatingFilterRule.Passes(G | R, SceneRating.R18G));
    }

    [Fact]
    public void AnEmptySetShowsEverythingRatherThanNothing()
    {
        // Never stored or offered, but a set that somehow ends up empty must not hide the library.
        Assert.True(RatingFilterRule.Passes(0, SceneRating.General));
        Assert.True(RatingFilterRule.Passes(0, SceneRating.R18G));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ARatingOutsideTheScalePassesOnlyWhenAllAreShown(int rating)
    {
        Assert.True(RatingFilterRule.Passes(RatingSet.All, rating));
        Assert.False(RatingFilterRule.Passes(G | R, rating));
    }

    [Fact]
    public void TheFlagsAreTheThreeRatingsAndAllIsAllThree()
    {
        Assert.Equal(RatingSet.All, G | R | RG);
        Assert.Equal(RatingSet.General, RatingFilterRule.Of(SceneRating.General));
        Assert.Equal(RatingSet.R18, RatingFilterRule.Of(SceneRating.R18));
        Assert.Equal(RatingSet.R18G, RatingFilterRule.Of(SceneRating.R18G));
    }

    // ------------------------------------------------------------ the settings switch

    [Theory]
    [InlineData(RatingSet.General)]
    [InlineData(RatingSet.R18 | RatingSet.R18G)]
    [InlineData(RatingSet.General | RatingSet.R18)]
    public void WithFilteringSwitchedOffEverythingPassesAndTheSetIsKept(RatingSet set)
    {
        Assert.Equal(RatingSet.All, RatingFilterRule.Effective(enabled: false, set));
        Assert.Equal(set, RatingFilterRule.Effective(enabled: true, set));
    }

    [Fact]
    public void AnEmptySetIsAllWhenItApplies()
        => Assert.Equal(RatingSet.All, RatingFilterRule.Effective(enabled: true, 0));

    // ------------------------------------------------------------ toggling a badge

    [Fact]
    public void ToggleTurnsARatingOnOrOff()
    {
        Assert.Equal(G | RG, RatingFilterRule.Toggle(RatingSet.All, R));
        Assert.Equal(G | R, RatingFilterRule.Toggle(G, R));
    }

    [Theory]
    [InlineData(RatingSet.General)]
    [InlineData(RatingSet.R18)]
    [InlineData(RatingSet.R18G)]
    public void TheLastRatingThatIsOnCannotBeTurnedOff(RatingSet only)
    {
        Assert.Equal(only, RatingFilterRule.Toggle(only, only));
        Assert.True(RatingFilterRule.IsLast(only, only));
    }

    [Fact]
    public void ARatingIsLastOnlyWhenItIsTheOneOn()
    {
        Assert.False(RatingFilterRule.IsLast(G | R, G));
        Assert.False(RatingFilterRule.IsLast(R, G));
    }

    // ------------------------------------------------------------ stored value, and the old five names

    [Theory]
    [InlineData("All", RatingSet.All)]
    [InlineData("GeneralOnly", RatingSet.General)]
    [InlineData("R18AndR18G", RatingSet.R18 | RatingSet.R18G)]
    [InlineData("R18Only", RatingSet.R18)]
    [InlineData("R18GOnly", RatingSet.R18G)]
    [InlineData("generalonly", RatingSet.General)]
    public void TheFiveOldNamesReadAsTheSameChoice(string stored, RatingSet expected)
        => Assert.Equal(expected, RatingFilterRule.Parse(stored));

    [Theory]
    [InlineData(RatingSet.General)]
    [InlineData(RatingSet.R18)]
    [InlineData(RatingSet.R18G)]
    [InlineData(RatingSet.General | RatingSet.R18)]
    [InlineData(RatingSet.General | RatingSet.R18G)]
    [InlineData(RatingSet.R18 | RatingSet.R18G)]
    [InlineData(RatingSet.All)]
    public void EverySetReadsBackTheSame(RatingSet set)
        => Assert.Equal(set, RatingFilterRule.Parse(RatingFilterRule.ToConfigValue(set)));

    [Fact]
    public void TheNewFormIsNotAnOldName()
    {
        // Written from now on: a set an older build would read as All rather than misread.
        Assert.Equal("G+R18", RatingFilterRule.ToConfigValue(G | R));
        Assert.Equal("G", RatingFilterRule.ToConfigValue(G));
        Assert.Equal("G+R18+R18G", RatingFilterRule.ToConfigValue(RatingSet.All));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Nonsense")]
    [InlineData("G+Nonsense")]
    [InlineData("7")]
    [InlineData("0")]
    [InlineData("+")]
    public void AMissingUnknownOrEmptyStoredValueIsAll(string? stored)
        => Assert.Equal(RatingSet.All, RatingFilterRule.Parse(stored));

    [Fact]
    public void TheNewFormIsReadWhateverItsCaseOrSpacing()
        => Assert.Equal(G | RG, RatingFilterRule.Parse(" r18g + g "));
}

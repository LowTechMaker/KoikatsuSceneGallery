using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// Which card represents a character. That single choice is what the gallery
/// shows — one character, one tile — so these cases are about the fallbacks
/// that guarantee a representative always exists, whatever the versions are
/// marked as.
/// </summary>
public sealed class CharacterVersionRankerTests
{
    private sealed record Card(
        string Name,
        int Day,
        CharacterVersionKind Kind = CharacterVersionKind.Current,
        bool Superseded = false);

    private static int Rank(List<Card> group)
        => CharacterVersionRanker.SortAndFindPrimary(
            group,
            card => new DateTime(2026, 1, card.Day),
            card => card.Kind,
            card => card.Superseded);

    private static List<Card> Group(params Card[] cards) => [.. cards];

    /// <summary>The names the gallery would show. Only ever the representative.</summary>
    private static List<string> Visible(List<Card> group)
    {
        var primary = Rank(group);
        return primary < 0 ? [] : [group[primary].Name];
    }

    private static int LiveAlternates(List<Card> group)
        => group.Count(card => CharacterVersionRanker.CountsAsLiveAlternate(card.Kind, card.Superseded));

    private static Card Alt(string name, int day, bool superseded = false)
        => new(name, day, CharacterVersionKind.Alternate, superseded);

    private static Card Old(string name, int day)
        => new(name, day, CharacterVersionKind.Current, Superseded: true);

    [Fact]
    public void SortsNewestFirst()
    {
        var group = Group(new("a", 1), new("c", 3), new("b", 2));

        Rank(group);

        Assert.Equal(["c", "b", "a"], group.Select(card => card.Name));
    }

    [Fact]
    public void WithNoAnnotationsOnlyTheNewestIsShown()
    {
        var group = Group(new("a", 1), new("b", 3), new("c", 2));

        Assert.Equal(["b"], Visible(group));
    }

    [Fact]
    public void AnEmptyGroupHasNoPrimary()
        => Assert.Equal(-1, Rank([]));

    [Fact]
    public void ASingleCardIsAlwaysShownWhateverItIsMarkedAs()
    {
        Assert.Equal(["a"], Visible(Group(new Card("a", 1))));
        Assert.Equal(["a"], Visible(Group(Old("a", 1))));
        Assert.Equal(["a"], Visible(Group(Alt("a", 1))));
        Assert.Equal(["a"], Visible(Group(Alt("a", 1, superseded: true))));
    }

    // The original requirement: a newer what-if must not push the original out.
    [Fact]
    public void ANewerWhatIfDoesNotTakeOverAndDoesNotAddATile()
    {
        var group = Group(new("original", 1), Alt("what-if", 5));

        Assert.Equal(["original"], Visible(group));
    }

    // What prompted collapsing the visibility rule: giving each marked version
    // its own tile scattered one character across the gallery.
    [Fact]
    public void ACharacterWithSeveralWhatIfsStillOccupiesOneTile()
    {
        var group = Group(new("original", 1), Alt("short-hair", 3), Alt("gender-swap", 5));

        Assert.Equal(["original"], Visible(group));
        Assert.Equal(2, LiveAlternates(group));
    }

    // Kind and superseded stay independent so a replaced what-if can say both,
    // which is what keeps it out of the badge.
    [Fact]
    public void AReplacedWhatIfNoLongerCountsAsOne()
    {
        var group = Group(new("original", 1), Alt("if-v1", 3, superseded: true), Alt("if-v2", 5));

        Assert.Equal(["original"], Visible(group));
        Assert.Equal(1, LiveAlternates(group));
    }

    // Every card replaced: the character must still appear, and the badge must
    // not claim it has live variants.
    [Fact]
    public void ACharacterWhoseEveryWhatIfIsReplacedStillAppearsWithoutTheBadge()
    {
        var group = Group(Alt("if-v1", 3, superseded: true), Alt("if-v2", 5, superseded: true));

        Assert.Equal(["if-v2"], Visible(group));
        Assert.Equal(0, LiveAlternates(group));
    }

    [Fact]
    public void TheNewestLiveCurrentRepresentsTheCharacter()
    {
        var group = Group(Old("v1", 1), new("v2", 4), Alt("if", 9));

        Assert.Equal(["v2"], Visible(group));
        Assert.Equal(1, LiveAlternates(group));
    }

    [Fact]
    public void ACurrentCardWinsEvenWhenAReplacedOneIsNewer()
    {
        var group = Group(new("current", 1), Old("newer-but-replaced", 9));

        Assert.Equal("current", group[Rank(group)].Name);
        Assert.Equal(["current"], Visible(group));
    }

    // Marking every card replaced must not make the character vanish.
    [Fact]
    public void WhenEveryCardIsReplacedTheNewestStillRepresentsIt()
    {
        var group = Group(Old("older", 1), Old("newer", 5));

        Assert.Equal("newer", group[Rank(group)].Name);
        Assert.Equal(["newer"], Visible(group));
    }

    // No current card at all: the newest live what-if stands in for it, and
    // still appears only once.
    [Fact]
    public void ALiveWhatIfRepresentsACharacterWithNoCurrentCard()
    {
        var group = Group(Old("retired", 1), Alt("if", 5));

        Assert.Equal("if", group[Rank(group)].Name);
        Assert.Equal(["if"], Visible(group));
    }

    [Fact]
    public void IdenticalTimestampsStillProduceExactlyOnePrimary()
    {
        var group = Group(new("a", 3), new("b", 3), new("c", 3));

        Assert.InRange(Rank(group), 0, group.Count - 1);
        Assert.Single(Visible(group));
    }

    [Fact]
    public void RankingIsStableWhenCalledRepeatedly()
    {
        var group = Group(new("a", 1), Alt("b", 5), new("c", 3));

        var first = group[Rank(group)].Name;
        var second = group[Rank(group)].Name;

        Assert.Equal(first, second);
        Assert.Equal("c", first);
    }

    [Fact]
    public void CountsAsLiveAlternateIsExhaustive()
    {
        Assert.True(CharacterVersionRanker.CountsAsLiveAlternate(CharacterVersionKind.Alternate, false));
        Assert.False(CharacterVersionRanker.CountsAsLiveAlternate(CharacterVersionKind.Alternate, true));
        Assert.False(CharacterVersionRanker.CountsAsLiveAlternate(CharacterVersionKind.Current, false));
        Assert.False(CharacterVersionRanker.CountsAsLiveAlternate(CharacterVersionKind.Current, true));
    }
}

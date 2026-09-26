using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class PokerHandTests
{
    private static IReadOnlyList<long> Sizes(int count) =>
        Enumerable.Range(1, count).Select(i => (long)i * 1000).ToArray();

    [Fact]
    public void JokerCandidatesTakesTheHeaviestTenth()
    {
        var top = PokerHand.JokerCandidates(Sizes(100), s => s);

        Assert.Equal(10, top.Count);
        Assert.Equal(100_000, top[0]);
        Assert.Equal(91_000, top[^1]);
    }

    [Fact]
    public void JokerCandidatesAlwaysOffersOneForASmallLibrary()
    {
        var top = PokerHand.JokerCandidates(Sizes(3), s => s);

        Assert.Single(top);
        Assert.Equal(3000, top[0]);
    }

    [Fact]
    public void JokerCandidatesOfAnEmptyLibraryIsEmpty()
        => Assert.Empty(PokerHand.JokerCandidates(Array.Empty<long>(), s => s));

    [Fact]
    public void JokerOddsLandNearTheDeclaredChance()
    {
        var rng = new Random(20260921);
        var hands = 20_000;
        var jokers = Enumerable.Range(0, hands).Count(_ => PokerHand.ShouldDealJoker(rng));

        var rate = (double)jokers / hands;
        Assert.InRange(rate, PokerHand.JokerChance - 0.02, PokerHand.JokerChance + 0.02);
    }

    [Fact]
    public void JokerSlotStaysInsideTheHand()
    {
        var rng = new Random(7);
        for (var i = 0; i < 500; i++)
            Assert.InRange(PokerHand.JokerSlot(rng, PokerHand.HandSize), 0, PokerHand.HandSize - 1);
    }

    [Fact]
    public void JokerSlotOfAnEmptyHandIsNoSlot()
        => Assert.Equal(-1, PokerHand.JokerSlot(new Random(1), 0));

    [Fact]
    public void HandSizeIsThirteen() => Assert.Equal(13, PokerHand.HandSize);
}

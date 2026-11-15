using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class HomeDealTests
{
    private sealed record Card(string Path, long Size);

    private static Card[] Library(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new Card($@"C:\lib\folder{i % 5}\{i}.png", 100_000 + i * 50_000))];

    private static IReadOnlyList<Card> Deal(HomeDeal dealer, IReadOnlyList<Card> candidates, IReadOnlyCollection<string> previous)
        => dealer.Deal(candidates, previous, c => c.Path, c => c.Size);

    private static string[] Paths(IEnumerable<Card> cards) => [.. cards.Select(c => c.Path)];

    [Fact]
    public void AHandHasPokerHandSizeDistinctCards()
    {
        var hand = Deal(new HomeDeal(new Random(1)), Library(100), []);

        Assert.Equal(PokerHand.HandSize, hand.Count);
        Assert.Equal(hand.Count, hand.Distinct().Count());
    }

    [Fact]
    public void ASmallLibraryIsDealtWhole()
    {
        var library = Library(5);

        var hand = Deal(new HomeDeal(new Random(1)), library, []);

        Assert.Equal(Paths(library).Order(), Paths(hand).Order());
    }

    [Fact]
    public void AnEmptyLibraryDealsNothing()
        => Assert.Empty(Deal(new HomeDeal(new Random(1)), [], []));

    [Fact]
    public void HandsWorkThroughTheLibraryWithoutRepeatsUntilItIsExhausted()
    {
        // A seed per run so the joker, which may replace a dealt card with an
        // earlier one, is part of what is checked over many seeds.
        for (var seed = 0; seed < 20; seed++)
        {
            var library = Library(PokerHand.HandSize * 4);
            var dealer = new HomeDeal(new Random(seed));
            string[] previous = [];
            var seen = new List<string>();
            for (var i = 0; i < 4; i++)
            {
                var hand = Paths(Deal(dealer, library, previous));
                Assert.Empty(hand.Intersect(previous, StringComparer.OrdinalIgnoreCase));
                seen.AddRange(hand);
                previous = hand;
            }
            // Jokers are drawn from outside the round, so a few cards can repeat
            // across hands; the round itself still covers most of the library.
            Assert.True(seen.Distinct().Count() >= library.Length - 4, $"seed {seed}");
        }
    }

    [Fact]
    public void ANewDealAvoidsThePreviousHandAcrossARoundBoundary()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            // Thirty cards: two hands leave four in the round, so the third
            // takes nine from a new round, which must not reuse the second
            // hand's thirteen while thirteen others exist.
            var library = Library(30);
            var dealer = new HomeDeal(new Random(seed));
            var first = Paths(Deal(dealer, library, []));
            var second = Paths(Deal(dealer, library, first));

            var third = Paths(Deal(dealer, library, second));

            Assert.Equal(PokerHand.HandSize, third.Length);
            Assert.Equal(third.Length, third.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Empty(third.Intersect(second, StringComparer.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void ALibraryTooSmallForTwoHandsRepeatsOnlyWhatItMust()
    {
        var library = Library(16);
        var dealer = new HomeDeal(new Random(3));
        var first = Paths(Deal(dealer, library, []));

        var second = Paths(Deal(dealer, library, first));

        Assert.Equal(PokerHand.HandSize, second.Length);
        Assert.Equal(second.Length, second.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Sixteen cards, thirteen just shown: all three others must be in the hand.
        Assert.Subset(second.ToHashSet(), Paths(library).Except(first).ToHashSet());
    }

    [Fact]
    public void CandidatesLeftOutAreNeverDealt()
    {
        // The view model leaves hidden cards out of the candidates; a card
        // hidden between deals must not come back from the shuffle's round.
        var library = Library(60);
        var dealer = new HomeDeal(new Random(7));
        Deal(dealer, library, []);
        var hidden = library.Where((_, i) => i % 2 == 0).Select(c => c.Path).ToHashSet();
        var visible = library.Where(c => !hidden.Contains(c.Path)).ToArray();

        for (var i = 0; i < 5; i++)
            Assert.Empty(Paths(Deal(dealer, visible, [])).Where(hidden.Contains));
    }

    [Fact]
    public void ANewCardJoinsTheRound()
    {
        var library = Library(13);
        var dealer = new HomeDeal(new Random(5));
        Deal(dealer, library, []);
        var added = new Card(@"C:\lib\new.png", 42);

        var hand = Deal(dealer, [.. library, added], []);

        Assert.Contains(added, hand);
    }
}

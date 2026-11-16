using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class HomeDuelTests
{
    private static DuelCard Card(string path, string? post = null, string? author = null, bool used = false)
        => new(path, post ?? "post:" + path, author ?? "author:" + path, used);

    private static readonly Func<string, bool> Nothing = _ => false;

    [Fact]
    public void FewerThanTwoCandidatesGiveNoPair()
    {
        var duel = new HomeDuel(new Random(1));

        Assert.Null(duel.Next([], Nothing));
        Assert.Null(duel.Next([Card("a")], Nothing));
        Assert.Null(duel.Next([Card("a"), Card("b")], path => path == "b"));
    }

    [Fact]
    public void ExcludedCardsNeverAppear()
    {
        var cards = Enumerable.Range(0, 20).Select(i => Card($"c{i}")).ToList();
        var duel = new HomeDuel(new Random(2));

        for (var i = 0; i < 30; i++)
        {
            var pair = duel.Next(cards, path => path.EndsWith('3') || path.EndsWith('7'))!;
            Assert.False("37".Contains(pair.Left.Path[^1]));
            Assert.False("37".Contains(pair.Right.Path[^1]));
        }
    }

    [Fact]
    public void TwoCardsOfOnePostAreNeverPaired()
    {
        var duel = new HomeDuel(new Random(3));

        Assert.Null(duel.Next([Card("a", post: "post:1"), Card("b", post: "post:1")], Nothing));

        for (var seed = 0; seed < 50; seed++)
        {
            var pair = new HomeDuel(new Random(seed)).Next(
                [Card("a", post: "post:1"), Card("b", post: "post:1"), Card("c", post: "post:2")], Nothing)!;
            Assert.NotEqual(pair.Left.PostKey, pair.Right.PostKey);
        }
    }

    [Fact]
    public void CardsWithoutAPostAreNotTreatedAsOnePost()
    {
        var pair = new HomeDuel(new Random(4)).Next(
            [new DuelCard("a", null, "x", false), new DuelCard("b", null, "y", false)], Nothing);

        Assert.NotNull(pair);
    }

    [Fact]
    public void TheSameAuthorIsAvoidedWhenItCanBe()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var pair = new HomeDuel(new Random(seed)).Next(
                [Card("a1", author: "A"), Card("a2", author: "A"), Card("a3", author: "A"), Card("b1", author: "B")],
                Nothing)!;
            Assert.NotEqual(pair.Left.AuthorKey, pair.Right.AuthorKey);
        }
    }

    [Fact]
    public void TheSameAuthorIsAllowedWhenNothingElseIsLeft()
    {
        var pair = new HomeDuel(new Random(5)).Next([Card("a1", author: "A"), Card("a2", author: "A")], Nothing);

        Assert.NotNull(pair);
    }

    [Fact]
    public void CardsNeverUsedComeFirst()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var cards = Enumerable.Range(0, 10).Select(i => Card($"used{i}", used: true))
                .Concat([Card("fresh1"), Card("fresh2")]).ToList();

            var pair = new HomeDuel(new Random(seed)).Next(cards, Nothing)!;

            Assert.Equal(["fresh1", "fresh2"], new[] { pair.Left.Path, pair.Right.Path }.Order());
        }
    }

    [Fact]
    public void UsedCardsAreShownOnceTheFreshOnesRunOut()
    {
        var duel = new HomeDuel(new Random(6));
        var cards = new[] { Card("fresh"), Card("used1", used: true), Card("used2", used: true) };

        var pair = duel.Next(cards, Nothing)!;

        Assert.Contains("fresh", new[] { pair.Left.Path, pair.Right.Path });
    }

    [Fact]
    public void NoCardRepeatsUntilThePoolIsExhausted()
    {
        var cards = Enumerable.Range(0, 10).Select(i => Card($"c{i}")).ToList();
        var duel = new HomeDuel(new Random(7));
        var seen = new List<string>();

        for (var i = 0; i < 5; i++)
        {
            var pair = duel.Next(cards, Nothing)!;
            seen.Add(pair.Left.Path);
            seen.Add(pair.Right.Path);
        }

        Assert.Equal(10, seen.Distinct().Count());
        // Exhausted: a new round starts instead of running dry.
        Assert.NotNull(duel.Next(cards, Nothing));
    }

    [Fact]
    public void AnOddCardOutStartsANewRoundRatherThanEndingTheGame()
    {
        var cards = Enumerable.Range(0, 3).Select(i => Card($"c{i}")).ToList();
        var duel = new HomeDuel(new Random(8));

        Assert.NotNull(duel.Next(cards, Nothing));
        Assert.NotNull(duel.Next(cards, Nothing));
    }

    [Fact]
    public void PicksAreCountedAndSkipsAreNot()
    {
        var duel = new HomeDuel(new Random(9));

        duel.Pick();
        duel.Pick();

        Assert.Equal(2, duel.Picked);
    }

    [Fact]
    public void ANewSessionForgetsWhatWasShown()
    {
        var cards = new[] { Card("a"), Card("b") };
        var duel = new HomeDuel(new Random(10));
        duel.Next(cards, Nothing);
        duel.Pick();

        duel.Reset();

        Assert.Empty(duel.Shown);
        Assert.Equal(0, duel.Picked);
    }
}

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class HomeCardCountsTests
{
    private static CountedCard Card(string path, string? author, string? post, bool counts = true)
        => new(path, author, post, counts);

    [Fact]
    public void OthersAreTheCountedCardsWithTheSameAuthorOrPostLessTheCardItself()
    {
        var counts = HomeCardCounts.Build([
            Card("a1", "A", "post:1"), Card("a2", "A", "post:1"), Card("a3", "A", "post:2"),
            Card("b1", "B", "post:3"),
        ]);

        Assert.Equal(2, counts.AuthorOthers("a1", "A"));
        Assert.Equal(1, counts.PostOthers("a1", "post:1"));
        Assert.Equal(0, counts.AuthorOthers("b1", "B"));
        Assert.Equal(0, counts.PostOthers("a3", "post:2"));
    }

    [Fact]
    public void CardsThatDoNotCountAreNotInTheNumber()
    {
        // Filtered out by rating, or "not for me" while hidden cards are not shown:
        // the number is what the user would find if they went to look.
        var counts = HomeCardCounts.Build([
            Card("a1", "A", "post:1"), Card("a2", "A", "post:1", counts: false),
            Card("a3", "A", "post:1", counts: false), Card("a4", "A", "post:2"),
        ]);

        Assert.Equal(1, counts.AuthorOthers("a1", "A"));
        Assert.Equal(0, counts.PostOthers("a1", "post:1"));
    }

    [Fact]
    public void ACardThatItselfDoesNotCountStillSeesAllCountedOthers()
    {
        var counts = HomeCardCounts.Build([Card("a1", "A", "post:1", counts: false), Card("a2", "A", "post:1")]);

        Assert.Equal(1, counts.AuthorOthers("a1", "A"));
        Assert.Equal(1, counts.PostOthers("a1", "post:1"));
    }

    [Fact]
    public void OnlyRealPostsAreCountedAsAPost()
    {
        // A card outside any post gets a folder or file key, which is not "the same post".
        var counts = HomeCardCounts.Build([Card("a1", null, "folder:x"), Card("a2", null, "folder:x")]);

        Assert.Equal(0, counts.PostOthers("a1", "folder:x"));
        Assert.Equal(0, counts.AuthorOthers("a1", null));
        Assert.Equal(0, counts.PostOthers("a1", null));
    }

    [Fact]
    public void PathsAndKeysCompareWithoutCase()
    {
        var counts = HomeCardCounts.Build([Card(@"C:\X\a1.png", "A", "post:1"), Card(@"C:\X\a2.png", "a", "POST:1")]);

        Assert.Equal(1, counts.AuthorOthers(@"c:\x\A1.PNG", "A"));
        Assert.Equal(1, counts.PostOthers(@"c:\x\A1.PNG", "post:1"));
    }

    [Fact]
    public void ADuplicatePathCountsOnce()
    {
        var counts = HomeCardCounts.Build([Card("a1", "A", "post:1"), Card("a1", "A", "post:1"), Card("a2", "A", "post:1")]);

        Assert.Equal(1, counts.AuthorOthers("a1", "A"));
    }

    [Fact]
    public void TwoBuildsOfTheSameCountsAreTheSameAndAChangeIsNot()
    {
        CountedCard[] cards = [Card("a1", "A", "post:1"), Card("a2", "A", "post:1")];

        Assert.True(HomeCardCounts.Build(cards).SameAs(HomeCardCounts.Build(cards.Reverse())));
        Assert.False(HomeCardCounts.Build(cards).SameAs(HomeCardCounts.Build([cards[0], cards[1] with { Counts = false }])));
        Assert.False(HomeCardCounts.Build(cards).SameAs(HomeCardCounts.Build([cards[0]])));
        Assert.False(HomeCardCounts.Build(cards).SameAs(null));
    }
}

using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class DetailNavigationTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(2, true, false)]
    public void NavigationStateUsesCurrentListPosition(int index, bool previous, bool next)
    {
        IList<CoordinateCard> cards = Cards();
        Assert.Equal((previous, next), DetailNavigationHelper.GetNavigationState(cards, cards[index]));
    }

    [Fact]
    public void NavigationStateDoesNotUsePathToReplaceMissingInstance()
    {
        IList<CoordinateCard> cards = Cards();
        Assert.Equal((false, false), DetailNavigationHelper.GetNavigationState(cards, Card("a.png")));
        Assert.Equal((false, false), DetailNavigationHelper.GetNavigationState(cards, null));
        Assert.Equal((false, false), DetailNavigationHelper.GetNavigationState(new List<CoordinateCard>(), cards[0]));
    }

    [Theory]
    [InlineData(0, -1, -1)]
    [InlineData(0, 1, 1)]
    [InlineData(1, -1, 0)]
    [InlineData(1, 1, 2)]
    [InlineData(2, 1, -1)]
    [InlineData(1, 0, 1)]
    public void NavigationPreservesListOrderAndBounds(int index, int direction, int expected)
    {
        IList<CoordinateCard> cards = Cards();
        var actual = DetailNavigationHelper.Navigate(cards, cards[index], direction);
        if (expected < 0) Assert.Null(actual);
        else Assert.Same(cards[expected], actual);
    }

    [Fact]
    public void MissingAndNullCurrentKeepTheirDifferentNavigationBehavior()
    {
        IList<CoordinateCard> cards = Cards();
        // Preserve the existing index -1 + direction policy; this is not the
        // BrowseContext fallback policy and not a path-based reload lookup.
        var missing = Card("a.png");
        Assert.Same(cards[0], DetailNavigationHelper.Navigate(cards, missing, 1));
        Assert.Null(DetailNavigationHelper.Navigate(cards, missing, -1));
        Assert.Null(DetailNavigationHelper.Navigate(cards, null, 1));
        Assert.Null(DetailNavigationHelper.Navigate(new List<CoordinateCard>(), missing, 1));
    }

    [Fact]
    public void RandomWithTwoCardsMustSelectTheOtherInstance()
    {
        IList<CoordinateCard> cards = new List<CoordinateCard> { Card("a.png"), Card("b.png") };
        Assert.Same(cards[1], DetailNavigationHelper.RandomCard(cards, cards[0]));
        Assert.Same(cards[0], DetailNavigationHelper.RandomCard(cards, cards[1]));
        Assert.Contains(DetailNavigationHelper.RandomCard(cards, null), cards);
    }

    [Fact]
    public void RandomHandlesEmptyAndSingleItemWithoutExcludingTheOnlyCard()
    {
        var card = Card("a.png");
        IList<CoordinateCard> single = new List<CoordinateCard> { card };
        Assert.Same(card, DetailNavigationHelper.RandomCard(single, card));
        Assert.Same(card, DetailNavigationHelper.RandomCard(single, null));
        Assert.Null(DetailNavigationHelper.RandomCard(new List<CoordinateCard>(), card));
    }

    private static IList<CoordinateCard> Cards() => new List<CoordinateCard>
    {
        Card("a.png"), Card("b.png"), Card("c.png")
    };
    private static CoordinateCard Card(string path) => new() { FilePath = path };
}

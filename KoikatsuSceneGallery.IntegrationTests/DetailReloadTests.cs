using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.IntegrationTests;

public sealed class DetailReloadTests
{
    [Theory]
    [InlineData("CARD.png")]
    [InlineData("card.png")]
    public void ReplacementUsesPathAndShowsNewInstance(string replacementPath)
    {
        var current = Card("card.png");
        var replacement = Card(replacementPath);
        var events = new List<string>();
        DetailNavigationHelper.RefreshAfterReload(new[] { Card("other.png"), replacement }, current,
            card => { Assert.Same(replacement, card); events.Add("show"); },
            () => events.Add("refresh"), () => events.Add("missing"));
        Assert.Equal(new[] { "show" }, events);
    }

    [Fact]
    public void SameInstanceOnlyRefreshesCurrentPage()
    {
        var current = Card("card.png");
        var events = new List<string>();
        DetailNavigationHelper.RefreshAfterReload(new[] { current }, current,
            _ => events.Add("show"),
            () => { events.Add("siblings"); events.Add("navigation"); },
            () => events.Add("missing"));
        Assert.Equal(new[] { "siblings", "navigation" }, events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingCardOnlyInvokesMissingCallback(bool empty)
    {
        var events = new List<string>();
        DetailNavigationHelper.RefreshAfterReload(empty ? [] : new[] { Card("other.png") }, Card("card.png"),
            _ => events.Add("show"), () => events.Add("refresh"), () => events.Add("missing"));
        Assert.Equal(new[] { "missing" }, events);
    }

    [Fact]
    public void FirstMatchingPathWinsEvenIfCurrentInstanceAppearsLater()
    {
        var current = Card("card.png");
        var first = Card("card.png");
        CoordinateCard? shown = null;
        DetailNavigationHelper.RefreshAfterReload(new[] { first, current }, current,
            card => shown = card, () => Assert.Fail("Unexpected refresh"), () => Assert.Fail("Unexpected missing"));
        Assert.Same(first, shown);
    }

    [Fact]
    public void StopsEnumeratingAfterMatch()
    {
        var current = Card("card.png");
        bool refreshed = false;
        DetailNavigationHelper.RefreshAfterReload(Sequence(), current,
            _ => Assert.Fail("Unexpected replacement"), () => refreshed = true, () => Assert.Fail("Unexpected missing"));
        Assert.True(refreshed);
        IEnumerable<CoordinateCard> Sequence()
        {
            yield return current;
            throw new InvalidOperationException("Enumeration continued past first match");
        }
    }

    [Fact]
    public void EnumerationAndCallbackErrorsPropagate()
    {
        var expected = new InvalidOperationException("failure");
        var current = Card("card.png");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            DetailNavigationHelper.RefreshAfterReload(Sequence(), current, _ => { }, () => { }, () => { })));
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            DetailNavigationHelper.RefreshAfterReload(new[] { current }, current, _ => { }, () => throw expected, () => { })));
        IEnumerable<CoordinateCard> Sequence()
        {
            yield return Card("other.png");
            throw expected;
        }
    }

    private static CoordinateCard Card(string path) => new() { FilePath = path };
}

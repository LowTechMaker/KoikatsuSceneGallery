using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class FilmstripPolicyTests
{
    [Theory]
    [InlineData(100, 50, 3, 47, 7)]
    [InlineData(100, 0, 3, 0, 7)]
    [InlineData(100, 1, 3, 0, 7)]
    [InlineData(100, 99, 3, 93, 7)]
    [InlineData(5, 2, 3, 0, 5)]
    [InlineData(1, 0, 3, 0, 1)]
    public void WindowStaysFullAndInsideTheList(int total, int index, int radius, int start, int count)
    {
        Assert.Equal((start, count), FilmstripPolicy.Window(total, index, radius));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 10)]
    public void WindowIsEmptyWithoutACurrentCard(int total, int index)
    {
        Assert.Equal((0, 0), FilmstripPolicy.Window(total, index, 3));
    }

    [Fact]
    public void CardsOutsideTheCurrentFolderAreDarkened()
    {
        string[] keys = ["a", "post:1", "POST:1", "post:1", "b"];
        Assert.Equal([true, false, false, false, true], FilmstripPolicy.Dimmed(keys, 2));
    }

    [Fact]
    public void ACardOnItsOwnDarkensNothing()
    {
        string[] keys = ["a", "b", "c"];
        Assert.Equal([false, false, false], FilmstripPolicy.Dimmed(keys, 1));
        Assert.Equal([false, false, false], FilmstripPolicy.Dimmed(keys, -1));
    }

    [Fact]
    public void AFolderFillingTheStripDarkensNothing()
    {
        string[] keys = ["f", "f", "f"];
        Assert.Equal([false, false, false], FilmstripPolicy.Dimmed(keys, 0));
    }
}

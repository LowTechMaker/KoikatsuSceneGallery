using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class ShuffleQueueComparerTests
{
    [Fact]
    public void Compare_UsesMappedOrderAndPlacesUnknownItemsLast()
    {
        object first = new();
        object second = new();
        var comparer = new ShuffleQueueComparer(new Dictionary<object, int>
        {
            [first] = 1,
            [second] = 3,
        });

        Assert.True(comparer.Compare(first, second) < 0);
        Assert.True(comparer.Compare(second, new object()) < 0);
        Assert.Equal(0, comparer.Compare(new object(), null));
    }

    [Fact]
    public void Compare_ItemsWithSameOrderAreEqual()
    {
        object left = new();
        object right = new();
        var comparer = new ShuffleQueueComparer(new Dictionary<object, int> { [left] = 2, [right] = 2 });

        Assert.Equal(0, comparer.Compare(left, right));
    }
}

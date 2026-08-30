using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class BoundedLruCacheTests
{
    [Fact]
    public void GetOrAddReturnsTheExistingValueWithoutRecreatingIt()
    {
        var cache = new BoundedLruCache<string, object>(2);
        var created = 0;

        var first = cache.GetOrAdd("thumbnail-a", _ =>
        {
            created++;
            return new object();
        });
        var again = cache.GetOrAdd("thumbnail-a", _ =>
        {
            created++;
            return new object();
        });

        Assert.Same(first, again);
        Assert.Equal(1, created);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void AddingPastCapacityEvictsTheLeastRecentlyUsedEntry()
    {
        var cache = new BoundedLruCache<string, int>(2);

        cache.GetOrAdd("a", _ => 1);
        cache.GetOrAdd("b", _ => 2);
        Assert.True(cache.TryGetValue("a", out _));

        cache.GetOrAdd("c", _ => 3);

        Assert.True(cache.TryGetValue("a", out var a));
        Assert.False(cache.TryGetValue("b", out _));
        Assert.True(cache.TryGetValue("c", out var c));
        Assert.Equal(1, a);
        Assert.Equal(3, c);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void RejectsAnInvalidCapacity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedLruCache<string, int>(0));
    }
}

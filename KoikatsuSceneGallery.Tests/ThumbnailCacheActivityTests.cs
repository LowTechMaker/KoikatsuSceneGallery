using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class ThumbnailCacheActivityTests
{
    [Fact]
    public void LeasesReportTheGlobalActiveCountUntilEachCompletes()
    {
        var activity = new ThumbnailCacheActivity();
        var observedCounts = new List<int>();
        activity.ActiveWorkCountChanged += observedCounts.Add;

        using var first = activity.Begin();
        using var second = activity.Begin();

        Assert.True(activity.IsCaching);
        Assert.Equal(2, activity.ActiveWorkCount);

        first.Dispose();

        Assert.True(activity.IsCaching);
        Assert.Equal(1, activity.ActiveWorkCount);

        second.Dispose();

        Assert.False(activity.IsCaching);
        Assert.Equal(0, activity.ActiveWorkCount);
        Assert.Equal([1, 2, 1, 0], observedCounts);
    }

    [Fact]
    public void DisposingTheSameLeaseTwiceDoesNotReduceTheCountTwice()
    {
        var activity = new ThumbnailCacheActivity();
        var lease = activity.Begin();

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(0, activity.ActiveWorkCount);
    }

    [Fact]
    public void ShutdownCancelsTheSharedWorkToken()
    {
        var activity = new ThumbnailCacheActivity();

        activity.CancelForShutdown();

        Assert.True(activity.ShutdownToken.IsCancellationRequested);
    }
}

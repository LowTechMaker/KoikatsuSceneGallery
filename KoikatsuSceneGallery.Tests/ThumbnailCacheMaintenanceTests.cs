using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ThumbnailCacheMaintenanceTests
{
    [Fact]
    public void MeasureAndClear_UseOnlyTopLevelJpegs()
    {
        using var directory = new TestDirectory();
        directory.Write("a.jpg", new byte[10]);
        directory.Write("b.jpg", new byte[20]);
        var original = directory.Write("original.png", new byte[40]);
        var pending = directory.Write("pending.tmp", new byte[50]);
        var nested = directory.Write("nested/keep.jpg", new byte[60]);

        Assert.Equal(new ThumbnailCacheUsage(2, 30), ThumbnailCacheMaintenance.Measure(directory.Path));
        Assert.Equal(new ThumbnailCacheClearResult(2, 0), ThumbnailCacheMaintenance.Clear(directory.Path));
        Assert.Equal(new ThumbnailCacheUsage(0, 0), ThumbnailCacheMaintenance.Measure(directory.Path));
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(pending));
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void MissingFolder_IsEmpty()
    {
        using var directory = new TestDirectory();
        var missing = Path.Combine(directory.Path, "missing");
        Assert.Equal(default, ThumbnailCacheMaintenance.Measure(missing));
        Assert.Equal(default, ThumbnailCacheMaintenance.Clear(missing));
    }

    [Fact]
    public void Clear_LockedFileReportsPartialFailureAndCanBeRetried()
    {
        if (!OperatingSystem.IsWindows()) return; // Unix permits unlinking an open file.
        using var directory = new TestDirectory();
        var locked = directory.Write("locked.jpg", new byte[10]);
        directory.Write("free.jpg", new byte[20]);
        var failures = new List<string>();
        using (var stream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = ThumbnailCacheMaintenance.Clear(directory.Path, (_, path) => failures.Add(path));
            Assert.Equal(new ThumbnailCacheClearResult(1, 1), result);
            Assert.Equal([locked], failures);
            Assert.Equal(new ThumbnailCacheUsage(1, 10), ThumbnailCacheMaintenance.Measure(directory.Path));
        }
        Assert.Equal(new ThumbnailCacheClearResult(1, 0), ThumbnailCacheMaintenance.Clear(directory.Path));
    }

    [Fact]
    public void Clear_CancellationPreservesFiles()
    {
        using var directory = new TestDirectory();
        var path = directory.Write("keep.jpg", new byte[10]);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            ThumbnailCacheMaintenance.Clear(directory.Path, cancellationToken: cancellation.Token));
        Assert.True(File.Exists(path));
    }
}

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class PathWriteLockTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(5);

    private static string UniquePath() => Path.Combine(Path.GetTempPath(), $"pwl-{Guid.NewGuid():N}.json");

    [Fact]
    public void SamePathAlwaysYieldsTheSameLockAndDifferentPathsDoNot()
    {
        var path = UniquePath();
        var other = UniquePath();

        Assert.Same(PathWriteLock.For(path), PathWriteLock.For(path));
        Assert.NotSame(PathWriteLock.For(path), PathWriteLock.For(other));
    }

    [Fact]
    public void PathsDifferingOnlyByCaseShareTheLockExactlyWhereThePlatformCallsThemOneFile()
    {
        var path = UniquePath();

        var shared = ReferenceEquals(PathWriteLock.For(path), PathWriteLock.For(path.ToUpperInvariant()));

        Assert.Equal(OperatingSystem.IsWindows(), shared);
    }

    [Fact]
    public async Task ASecondWaiterOnTheSamePathOnlyEntersAfterTheFirstReleases()
    {
        var path = UniquePath();
        var gate = PathWriteLock.For(path);
        Assert.True(await gate.WaitAsync(Limit));

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiter = Task.Run(async () =>
        {
            await PathWriteLock.For(path).WaitAsync(Limit);
            entered.SetResult();
        });

        // Still held by this test: the waiter cannot have entered.
        Assert.False(entered.Task.IsCompleted);

        gate.Release();

        await entered.Task.WaitAsync(Limit);
        await waiter.WaitAsync(Limit);
        PathWriteLock.For(path).Release();
    }

    [Fact]
    public async Task LocksForDifferentPathsDoNotBlockEachOther()
    {
        var held = PathWriteLock.For(UniquePath());
        var free = PathWriteLock.For(UniquePath());
        Assert.True(await held.WaitAsync(Limit));

        try
        {
            Assert.True(await free.WaitAsync(Limit));
            free.Release();
        }
        finally
        {
            held.Release();
        }
    }
}

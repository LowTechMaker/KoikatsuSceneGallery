using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportAnalysisTrackerTests
{
    [Fact]
    public void InterleavedBatchesRetainOnlyActiveProgress()
    {
        var tracker = new ImportAnalysisTracker();
        using var first = tracker.Begin(["a.png", "b.png"]);
        using var second = tracker.Begin(["c.png"]);
        first.AddRejectedCount(1);
        Assert.Equal(new ImportAnalysisProgress(true, 3, 2), tracker.GetProgress(["a.png"]));
        second.Dispose();
        Assert.Equal(new ImportAnalysisProgress(true, 2, 2), tracker.GetProgress(["a.png", "c.png"]));
        first.Dispose();
        Assert.Equal(new ImportAnalysisProgress(false, 0, 0), tracker.GetProgress(["a.png"]));
    }

    [Fact]
    public void PathsAreUniqueCaseInsensitiveAndSharedPathsSurviveOneBatch()
    {
        var tracker = new ImportAnalysisTracker();
        using var first = tracker.Begin(["A.png", "a.png"]);
        using var second = tracker.Begin(["a.png", "b.png"]);
        Assert.True(tracker.ContainsPath("A.PNG"));
        Assert.Equal(new ImportAnalysisProgress(true, 2, 1), tracker.GetProgress(["A.PNG", "a.png", "other.png"]));
        first.Dispose();
        first.Dispose();
        Assert.True(tracker.ContainsPath("a.png"));
        Assert.Equal(2, tracker.GetProgress([]).TotalCount);
    }

    [Fact]
    public void RejectionsAreBoundedAndDisposedBatchCannotChangeProgress()
    {
        var tracker = new ImportAnalysisTracker();
        using var batch = tracker.Begin(["a", "b"]);
        batch.AddRejectedCount(-10);
        Assert.Equal(0, tracker.GetProgress([]).CompletedCount);
        batch.AddRejectedCount(int.MaxValue);
        batch.AddRejectedCount(int.MaxValue);
        Assert.Equal(2, tracker.GetProgress(["a", "b"]).CompletedCount);
        batch.Dispose();
        batch.AddRejectedCount(1);
        Assert.Equal(new ImportAnalysisProgress(false, 0, 0), tracker.GetProgress([]));
    }

    [Fact]
    public void CancelDoesNotFinishWorkOrCancelOtherBatches()
    {
        var tracker = new ImportAnalysisTracker();
        using var first = tracker.Begin(["a"]);
        using var second = tracker.Begin(["b"]);
        first.Cancel();
        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
        tracker.CancelAll();
        Assert.True(second.Token.IsCancellationRequested);
        Assert.Equal(new ImportAnalysisProgress(true, 2, 0), tracker.GetProgress([]));
        first.Dispose();
        Assert.True(tracker.GetProgress([]).IsAnalyzing);
        second.Dispose();
        tracker.CancelAll();
        Assert.False(tracker.GetProgress([]).IsAnalyzing);
    }

    [Fact]
    public void ResetKeepsCancellationOwnershipAndIgnoresOldCompletion()
    {
        var tracker = new ImportAnalysisTracker();
        using var old = tracker.Begin(["same"]);
        old.AddRejectedCount(1);
        tracker.ResetProgress();
        Assert.False(tracker.ContainsPath("same"));
        Assert.Equal(new ImportAnalysisProgress(true, 0, 0), tracker.GetProgress(["same"]));
        using var current = tracker.Begin(["same", "new"]);
        current.AddRejectedCount(1);
        old.AddRejectedCount(1);
        tracker.CancelAll();
        Assert.True(old.Token.IsCancellationRequested);
        old.Dispose();
        Assert.Equal(new ImportAnalysisProgress(true, 2, 1), tracker.GetProgress([]));
        Assert.True(tracker.ContainsPath("same"));
    }

    [Fact]
    public async Task ExceptionCleanupAllowsAnotherBatch()
    {
        var tracker = new ImportAnalysisTracker();
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            var batch = tracker.Begin(["a"]);
            try
            {
                await Task.Yield();
                throw new IOException("Expected");
            }
            finally { batch.Dispose(); }
        });
        Assert.False(tracker.GetProgress([]).IsAnalyzing);
        using var next = tracker.Begin(["a"]);
        Assert.Equal(new ImportAnalysisProgress(true, 1, 0), tracker.GetProgress([]));
    }
}

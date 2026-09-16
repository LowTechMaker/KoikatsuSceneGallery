using System.Diagnostics;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ResolveDiagnosticCollectorTests
{
    private static ImportLibraryIndexResult Index() => new([], [], 11, 22, 33, 44, 55, 66);

    [Fact]
    public void AccumulatesEachCategoryAndPreservesAllResultFields()
    {
        long ticks = 0;
        var collector = new ResolveDiagnosticCollector(() => ticks += Stopwatch.Frequency);
        var sentinel = new object();
        Assert.Same(sentinel, collector.MeasureArtworkDirectoryLookup(() => sentinel));
        Assert.Equal(7, collector.MeasureArtworkDirectoryLookup(() => 7));
        int assignments = 0;
        collector.MeasurePropertyAssignment(() => assignments++);
        collector.DestinationPathAssignments = 8;
        collector.StatusTransitionsToAlreadyInLibrary = 9;

        Assert.Equal(1, assignments);
        Assert.Equal(new ResolveDiagnosticResult(77, 11, 22, 33, 44, 55, 66, 88, 2000, 1000, 8, 9),
            collector.ToResult(77, 88, Index()));
    }

    [Fact]
    public void FailedOperationsStillAccumulateTimeAndPropagateOriginalException()
    {
        long ticks = 0;
        var collector = new ResolveDiagnosticCollector(() => ticks += Stopwatch.Frequency);
        var failure = new IOException("operation failed");
        Assert.Same(failure, Assert.Throws<IOException>(() =>
            collector.MeasureArtworkDirectoryLookup<int>(() => throw failure)));
        Assert.Same(failure, Assert.Throws<IOException>(() =>
            collector.MeasurePropertyAssignment(() => throw failure)));
        var result = collector.ToResult(0, 0, Index());
        Assert.Equal(1000, result.UiArtworkDirectoryLookupElapsedMs);
        Assert.Equal(1000, result.UiPropertyAssignmentElapsedMs);
        Assert.Equal(0, result.DestinationPathAssignments);
        Assert.Equal(0, result.StatusTransitionsToAlreadyInLibrary);
    }

    [Fact]
    public void NewPassStartsEmptyAndReadingAResultDoesNotResetCounters()
    {
        long ticks = 0;
        var collector = new ResolveDiagnosticCollector(() => ticks += Stopwatch.Frequency);
        collector.MeasurePropertyAssignment(() => { });
        var first = collector.ToResult(1, 2, Index());
        Assert.Equal(first, collector.ToResult(1, 2, Index()));
        var next = new ResolveDiagnosticCollector(() => throw new InvalidOperationException("no operation"));
        var result = next.ToResult(1, 2, Index());
        Assert.Equal(0, result.UiPropertyAssignmentElapsedMs);
        Assert.Equal(0, result.UiArtworkDirectoryLookupElapsedMs);
    }
}

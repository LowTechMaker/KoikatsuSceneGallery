using System.Diagnostics;

namespace KoikatsuSceneGallery.Services;

// One resolution pass; accessed serially by the existing UI publication.
internal sealed class ResolveDiagnosticCollector
{
    private readonly Func<long> _getTimestamp;

    internal ResolveDiagnosticCollector(Func<long>? getTimestamp = null)
        => _getTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;

    private long _artworkDirectoryLookupElapsedTicks;
    private long _propertyAssignmentElapsedTicks;

    public int DestinationPathAssignments { get; set; }
    public int StatusTransitionsToAlreadyInLibrary { get; set; }

    public T MeasureArtworkDirectoryLookup<T>(Func<T> operation)
    {
        var startedAt = _getTimestamp();
        try
        {
            return operation();
        }
        finally
        {
            _artworkDirectoryLookupElapsedTicks += _getTimestamp() - startedAt;
        }
    }

    public void MeasurePropertyAssignment(Action operation)
    {
        var startedAt = _getTimestamp();
        try
        {
            operation();
        }
        finally
        {
            _propertyAssignmentElapsedTicks += _getTimestamp() - startedAt;
        }
    }

    public ResolveDiagnosticResult ToResult(
        long backgroundIndexElapsedMs,
        long uiQueueDelayMs,
        ImportLibraryIndexResult libraryIndex)
        => new(
            backgroundIndexElapsedMs,
            libraryIndex.LibraryScanAndCandidateIndexElapsedMs,
            libraryIndex.ByteComparisonElapsedMs,
            libraryIndex.FolderIndexElapsedMs,
            libraryIndex.TotalCandidatesCompared,
            libraryIndex.ActualIdenticalDuplicates,
            libraryIndex.ActualContentMismatches,
            uiQueueDelayMs,
            (long)Stopwatch.GetElapsedTime(0, _artworkDirectoryLookupElapsedTicks).TotalMilliseconds,
            (long)Stopwatch.GetElapsedTime(0, _propertyAssignmentElapsedTicks).TotalMilliseconds,
            DestinationPathAssignments,
            StatusTransitionsToAlreadyInLibrary);
}


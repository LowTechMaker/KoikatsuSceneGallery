namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Timing and mutation counts captured for one destination-resolution pass.
/// </summary>
public sealed record ResolveDiagnosticResult(
    long BackgroundIndexElapsedMs,
    long BgLibraryScanAndCandidateIndexElapsedMs,
    long BgByteComparisonElapsedMs,
    long BgFolderIndexElapsedMs,
    int TotalCandidatesCompared,
    int ActualIdenticalDuplicates,
    int ActualContentMismatches,
    long UiQueueDelayMs,
    long UiArtworkDirectoryLookupElapsedMs,
    long UiPropertyAssignmentElapsedMs,
    int DestinationPathAssignments,
    int StatusTransitionsToAlreadyInLibrary);

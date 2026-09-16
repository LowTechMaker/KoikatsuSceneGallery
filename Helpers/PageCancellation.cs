namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Retires the cancellation source a page keeps for its current background
/// operation.
/// </summary>
/// <remarks>
/// Every page that loads card metadata or posts repeated the same three lines:
/// cancel, dispose, then either clear the field on navigation away or replace
/// it for the next card. Disposing without cancelling first leaves the running
/// operation unaware, and clearing without disposing leaks the registration, so
/// the order is the part worth keeping in one place.
///
/// Called on the UI thread only, like the fields it replaces.
/// </remarks>
internal static class PageCancellation
{
    /// <summary>Cancels and disposes the current source, leaving no replacement.</summary>
    internal static void Stop(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = null;
    }

    /// <summary>
    /// Cancels and disposes the current source and installs a fresh one, which
    /// is returned so the caller can take its token without a null check.
    /// </summary>
    internal static CancellationTokenSource Restart(ref CancellationTokenSource? source)
    {
        source?.Cancel();
        source?.Dispose();
        source = new CancellationTokenSource();
        return source;
    }
}

using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// One gallery's metadata pipeline: the scan coordinator, the refresh timer
/// that batches its UI updates, and the stop flag shutdown sets.
/// </summary>
/// <remarks>
/// The three metadata galleries held the coordinator and the timer as separate
/// fields and repeated the same lifecycle around them — start unless stopping,
/// queue unless stopping, stop the timer whenever the scan is cancelled, and
/// dispose both together. Only that lifecycle is shared.
///
/// Everything gallery specific stays with the caller: the coordinator is built
/// by the gallery (its own cache lookup, parser, apply and error mapping), and
/// the callbacks passed to <see cref="Start"/> and <see cref="Queue"/> keep each
/// gallery's filter policy — the scene gallery refreshes only when a metadata
/// filter is active, the other two refresh unconditionally.
///
/// Used from the owning UI dispatcher, like the members it replaces.
/// </remarks>
internal sealed class GalleryMetadataSession<TCard, TMetadata> : IDisposable
{
    private readonly MetadataScanCoordinator<TCard, TMetadata> _scan;
    private readonly GalleryMetadataRefresh _refresh;
    private readonly IAppLogger _logger;
    private readonly string _operationPrefix;
    private bool _stopping;

    internal GalleryMetadataSession(
        MetadataScanCoordinator<TCard, TMetadata> scan,
        GalleryMetadataRefresh refresh,
        IAppLogger logger,
        string operationPrefix)
    {
        _scan = scan;
        _refresh = refresh;
        _logger = logger;
        _operationPrefix = operationPrefix;
        _scan.PendingCountChanged += count => PendingCountChanged?.Invoke(count);
    }

    internal event Action<int>? PendingCountChanged;

    /// <summary>Scans every card that still lacks metadata.</summary>
    internal void Start(IEnumerable<TCard> cards, Action onEmpty)
    {
        if (_stopping) return;
        _scan.Start(cards, onEmpty, _refresh.Start, OnCompleted)
            .Observe(_logger, _operationPrefix + ".ParseMetadata");
    }

    /// <summary>Scans one card that arrived after the initial load.</summary>
    internal void Queue(TCard card, Action onCached)
    {
        if (_stopping) return;
        _scan.Queue(card, onCached, OnCompleted)
            .Observe(_logger, _operationPrefix + ".ParseAddedCardMetadata");
    }

    /// <summary>
    /// Refuses further work and waits for parses already in flight. The flag is
    /// permanent: shutdown owns this, and a gallery that is never a shutdown
    /// participant simply never sets it.
    /// </summary>
    internal Task StopAsync()
    {
        _stopping = true;
        _refresh.Stop();
        return _scan.StopAsync(CancellationToken.None);
    }

    /// <summary>Cancels the current scan without touching the pending count.</summary>
    internal void CancelScan() => _scan.Cancel();

    /// <summary>
    /// Cancels the current scan, clears the pending count and stops the refresh
    /// timer. A later <see cref="Start"/> begins a fresh scan.
    /// </summary>
    internal void Suspend()
    {
        _scan.Cancel(resetCount: true);
        _refresh.Stop();
    }

    private void OnCompleted() => _refresh.Complete();

    public void Dispose()
    {
        _scan.Dispose();
        _refresh.Dispose();
    }
}

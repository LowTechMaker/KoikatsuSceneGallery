namespace SceneGallery.PluginSdk;

/// <summary>What became of one re-fetched artwork.</summary>
public enum ArtworkRefreshStatus
{
    /// <summary>The platform answered with the artwork.</summary>
    Found,

    /// <summary>
    /// The platform says the artwork is not there: deleted, or made private.
    /// Asking again will not change that, so the host stops asking.
    /// </summary>
    Gone,

    /// <summary>
    /// The request did not get an answer about the artwork: offline, rate
    /// limited, a server error, or a response that could not be read. Asking
    /// again later may work.
    /// </summary>
    Failed,
}

/// <summary>The answer for one artwork; <see cref="Info"/> is set only when <see cref="Status"/> is Found.</summary>
public sealed record ArtworkRefreshResult(ArtworkRefreshStatus Status, ArtworkInfo? Info)
{
    public static ArtworkRefreshResult Found(ArtworkInfo info) => new(ArtworkRefreshStatus.Found, info);

    public static readonly ArtworkRefreshResult Gone = new(ArtworkRefreshStatus.Gone, null);

    public static readonly ArtworkRefreshResult Failed = new(ArtworkRefreshStatus.Failed, null);
}

/// <summary>
/// Optional extension for import providers whose already-imported artworks can
/// be fetched again, bypassing the plugin's own cache.
/// </summary>
/// <remarks>
/// Implementing this opts the plugin into the host's bulk re-fetch of artwork
/// metadata for cards already in the library. The host decides what to
/// re-fetch and writes the results; the plugin only answers for one artwork
/// at a time, under its usual rate limit.
///
/// The three outcomes must be kept apart. A long run meets many deleted
/// artworks, and the host stops a run after a streak of failures on the
/// assumption that something is wrong with the connection — reporting a
/// deleted artwork as a failure trips that for nothing, and hides which
/// artworks will never have data.
/// </remarks>
public interface IArtworkMetadataRefresher : ICardImportProvider
{
    /// <summary>
    /// Fetches <paramref name="id"/> from the provider even when a cached copy
    /// exists, replacing that copy when the artwork is found.
    /// </summary>
    Task<ArtworkRefreshResult> RefreshArtworkAsync(ArtworkId id, CancellationToken ct);
}

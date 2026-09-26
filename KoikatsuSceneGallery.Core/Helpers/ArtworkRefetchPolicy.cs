namespace KoikatsuSceneGallery.Helpers;

/// <summary>Which artworks a re-fetch run covers.</summary>
public enum ArtworkRefetchScope
{
    /// <summary>Every artwork, overwriting data already saved.</summary>
    All,

    /// <summary>Only artworks with no saved data; saved sidecars are not touched.</summary>
    MissingOnly,
}

/// <summary>
/// The rules of a bulk artwork re-fetch that do not depend on where the
/// artworks come from: what a resumed run may skip, when it must give up, and
/// how long it will take.
/// </summary>
internal static class ArtworkRefetchPolicy
{
    /// <summary>
    /// Consecutive failed artworks after which the run stops by itself.
    /// </summary>
    /// <remarks>
    /// Only requests that got no answer count — a deleted or private artwork
    /// is an answer, and counting those stopped the first real run within
    /// seconds of resuming. Twenty unanswered in a row is a connection gone or
    /// the platform refusing, and carrying on would only hammer it harder.
    /// </remarks>
    public const int MaxConsecutiveFailures = 20;

    /// <summary>
    /// Artworks written between rebuilds of the tag cloud during a run.
    /// </summary>
    /// <remarks>
    /// A rebuild reads every sidecar again, which takes seconds; at the
    /// limiter's pace two hundred artworks is a quarter of an hour or so.
    /// </remarks>
    public const int TagCloudRefreshEvery = 200;

    /// <summary>
    /// Seconds per artwork the estimate assumes: the plugins' shared limiter
    /// waits two seconds plus up to three of jitter between requests.
    /// </summary>
    public const double MinSecondsPerArtwork = 2;

    /// <inheritdoc cref="MinSecondsPerArtwork"/>
    public const double MaxSecondsPerArtwork = 5;

    /// <summary>
    /// Whether an artwork still has to be fetched in this run.
    /// </summary>
    /// <param name="fetchedAt">When its saved metadata was fetched; null when it has none.</param>
    /// <param name="resumeFrom">
    /// When the run being resumed started; null for a run from the start,
    /// which fetches everything.
    /// </param>
    /// <remarks>
    /// Anything fetched since the run started was fetched by that run, so a
    /// resumed run can skip it. An artwork that failed was not written and
    /// still carries its older time, so it is tried again.
    /// </remarks>
    public static bool NeedsFetch(DateTimeOffset? fetchedAt, DateTimeOffset? resumeFrom)
        => resumeFrom is null || fetchedAt is null || fetchedAt < resumeFrom;

    /// <inheritdoc cref="NeedsFetch(DateTimeOffset?, DateTimeOffset?)"/>
    /// <remarks>
    /// A missing-only run needs no memory of where it stopped: what it has
    /// fetched now has data, so a resumed one simply finds less missing.
    /// </remarks>
    public static bool NeedsFetch(DateTimeOffset? fetchedAt, DateTimeOffset? resumeFrom, ArtworkRefetchScope scope)
        => scope == ArtworkRefetchScope.MissingOnly
            ? fetchedAt is null
            : NeedsFetch(fetchedAt, resumeFrom);

    /// <summary>Hours the run is expected to take, as a range.</summary>
    public static (double Min, double Max) EstimatedHours(int artworks)
        => (artworks * MinSecondsPerArtwork / 3600, artworks * MaxSecondsPerArtwork / 3600);
}

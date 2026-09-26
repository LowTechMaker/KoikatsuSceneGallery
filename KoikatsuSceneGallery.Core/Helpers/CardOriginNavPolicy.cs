namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which source-specific navigation entries survive the current platform.
/// </summary>
/// <remarks>
/// Only the entries that are themselves source-specific are decided here. The
/// library entries are owned by the user's own <c>HiddenNavItems</c> preference
/// and are deliberately not touched, so the platform switch can never resurrect
/// an entry the user hid by hand.
///
/// Both per-platform pages follow the same rule: a page that can only ever show
/// one platform's data is offered only while the selected platform actually has
/// that data. Showing the entry and then an empty page is worse than not
/// offering it — it reads as a fault rather than as an absence.
/// </remarks>
public static class CardOriginNavPolicy
{
    /// <summary>
    /// The authors page.
    /// </summary>
    /// <param name="platformsWithAuthors">
    /// Platforms that have authors to list. Empty while no author plugin is
    /// installed.
    /// </param>
    public static bool ShowAuthors(
        CardOriginSelection origin, IEnumerable<string> platformsWithAuthors)
        => Offers(origin, platformsWithAuthors);

    /// <summary>
    /// The tag page. Tags come from the platforms' own sidecars, so a platform
    /// that never carried any — FANBOX, in the library this was written
    /// against — has nothing to show however its plugin is configured.
    /// </summary>
    public static bool ShowTags(
        CardOriginSelection origin, IEnumerable<string> platformsWithTags)
        => Offers(origin, platformsWithTags);

    /// <summary>
    /// The local sources page. Hidden while a remote platform is selected; it
    /// manages nothing else.
    /// </summary>
    public static bool ShowLocalSources(CardOriginSelection origin)
        => origin.IsAll || origin.IsLocal;

    /// <summary>
    /// Whether a per-platform page has anything to show for the current
    /// selection.
    /// </summary>
    /// <remarks>
    /// Under "all" the page keeps its own platform picker, so it is offered as
    /// long as any platform has data. Under a named platform it is offered only
    /// when that platform is one of them — local never is, since these pages
    /// list remote data by definition.
    /// </remarks>
    private static bool Offers(CardOriginSelection origin, IEnumerable<string> platforms)
    {
        if (origin.IsLocal) return false;
        if (origin.IsAll) return platforms.Any();
        return platforms.Any(id => string.Equals(id, origin.ProviderId, StringComparison.OrdinalIgnoreCase));
    }
}

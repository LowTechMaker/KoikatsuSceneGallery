namespace SceneGallery.PluginSdk;

/// <summary>
/// Optional plugin extension that knows how to find every post an author has
/// published at a platform, starting from the address of the author's page.
/// <para>
/// The app uses it to check an author's posts one by one against the cards a
/// user has dropped in. Listing is the cheap half: each post still has to be
/// fetched through <see cref="ICardImportProvider"/>, which is rate-limited.
/// </para>
/// </summary>
public interface IAuthorPostLister : IPlugin
{
    /// <summary>Provider identifier, the same one the plugin's <see cref="ICardImportProvider"/> uses.</summary>
    string ProviderId { get; }

    /// <summary>
    /// Extracts the author from the address of their page, e.g.
    /// "https://www.pixiv.net/users/12345678". Pure string work, no I/O.
    /// Returns null when the address is not an author page of this provider.
    /// </summary>
    AuthorKey? TryParseAuthorUrl(string url);

    /// <summary>
    /// Lists the author's posts, newest first. Returns null when the author
    /// cannot be listed (not found, offline, the platform refused); an author
    /// with no posts is an empty list, not null.
    /// </summary>
    Task<IReadOnlyList<ArtworkId>?> ListPostsAsync(AuthorKey author, CancellationToken ct);
}

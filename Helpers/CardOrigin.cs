using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Where a card came from, read from the author resolved off its folder.
/// </summary>
/// <remarks>
/// The single place that answers "is this card local?". Detail pages, grouping
/// and the origin filter all need it, and the answer must be the same in each:
/// a local card that looked remote in one of them would leak a private file
/// into an outbound link or an artwork group.
/// </remarks>
internal static class CardOrigin
{
    public static string? ProviderIdOf(object? card)
        => (card as IAuthorOwner)?.Author?.Key.ProviderId;

    public static bool IsLocal(object? card)
        => LocalSourceIdentity.IsLocal(ProviderIdOf(card));

    /// <summary>
    /// Outbound links derived from a file name, or none for a local card.
    /// </summary>
    /// <remarks>
    /// The link parser only looks at the file name, so a privately shared file
    /// that happens to contain a digit run would otherwise grow a working
    /// pixiv button. Local sources are never resolved remotely, so they get no
    /// links at all.
    /// </remarks>
    public static FilenameLinkInfo LinksFor(CardBase? card)
        => IsLocal(card)
            ? FilenameLinkParser.Empty
            : FilenameLinkParser.Parse(card?.FilePath);
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// A card's grouping identity, resolved through the provider that owns it.
/// </summary>
/// <remarks>
/// The single implementation on purpose. The artwork id can only come from the
/// provider's own folder-name and file-name parsers — deriving it from the file
/// name alone agrees with the provider for pixiv but not for bepisdb, whose
/// artwork ids are composite. Two callers doing it two ways meant the tag page
/// found no bepisdb cards at all while the grid grouped them correctly.
/// </remarks>
internal static class CardGroupKey
{
    public static string For(CardBase card, PluginService plugins, IAppLogger logger)
    {
        var author = (card as IAuthorOwner)?.Author;
        try
        {
            var provider = plugins.ImportProviders
                .FirstOrDefault(p => p.ProviderId == author?.Key.ProviderId);
            return GalleryGrouping.ResolveKey(card.FilePath, provider?.ProviderId,
                provider?.TryParseArtworkFolderName(Path.GetFileName(Path.GetDirectoryName(card.FilePath)) ?? "")?.Id,
                provider?.TryParseFilename(card.FileName)?.Id, author?.Key.Id);
        }
        catch (Exception ex)
        {
            logger.LogError("CardGroupKey.Resolve", ex, card.FilePath);
            return "folder:" + Path.GetDirectoryName(card.FilePath);
        }
    }
}

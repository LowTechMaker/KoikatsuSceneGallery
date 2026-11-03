using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

internal static class PostMetadataMapper
{
    public static PostMetadataDocument ToDocument(
        ArtworkInfo info,
        IReadOnlyList<string> localFileNames)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(localFileNames);

        return new PostMetadataDocument(
            PostMetadataDocument.CurrentSchemaVersion,
            info.ArtworkId.ProviderId,
            info.ArtworkId.Id,
            info.AuthorName,
            info.AuthorId,
            info.Title,
            info.Description,
            (int)info.Rating,
            info.Tags.Select(static tag => new PostMetadataTag(tag.Name, tag.TranslatedName)).ToList(),
            info.FetchedAt)
        {
            LocalFileNames = localFileNames,
        };
    }

    /// <summary>
    /// The sidecar an import writes for <paramref name="item"/>, or null when
    /// nothing was fetched for it. The platform rating stays in
    /// <see cref="PostMetadataDocument.Rating"/>; the rating the user settled on
    /// in review is recorded as <see cref="PostMetadataDocument.UserRating"/>
    /// only when it differs, so an untouched card keeps following the platform.
    /// </summary>
    public static PostMetadataDocument? ToImportDocument(
        ImportItem item,
        IReadOnlyList<string> localFileNames)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.FetchedArtworkInfo is not { } info)
            return null;

        var document = ToDocument(info, localFileNames);
        return item.Rating == info.Rating
            ? document
            : document with { UserRating = (int)item.Rating };
    }
}

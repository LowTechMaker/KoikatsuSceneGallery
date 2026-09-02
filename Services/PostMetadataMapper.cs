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
}

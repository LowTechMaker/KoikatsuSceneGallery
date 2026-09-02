namespace KoikatsuSceneGallery.Models;

internal sealed record PostMetadataTag(
    string Name,
    string? TranslatedName);

internal sealed record PostMetadataDocument(
    int SchemaVersion,
    string ProviderId,
    string ArtworkId,
    string AuthorName,
    string AuthorId,
    string? Title,
    string? Description,
    int Rating,
    IReadOnlyList<PostMetadataTag> Tags,
    DateTimeOffset FetchedAt)
{
    public const int CurrentSchemaVersion = 1;
}

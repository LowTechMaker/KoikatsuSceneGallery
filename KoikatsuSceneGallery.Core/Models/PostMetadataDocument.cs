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
    public const int CurrentSchemaVersion = 2;

    /// <summary>
    /// Local card file names belonging to this artwork. Version 2 added this
    /// information so metadata can still be associated with manually named
    /// files that do not embed an artwork identifier.
    /// </summary>
    public IReadOnlyList<string> LocalFileNames { get; init; } = [];
}

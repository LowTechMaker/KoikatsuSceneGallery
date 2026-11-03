using System.Text.Json.Serialization;

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

    /// <summary>
    /// The rating the user chose by hand, or null when they never overrode the
    /// platform. Kept apart from <see cref="Rating"/> on purpose: that one is
    /// the platform's own value and a re-fetch replaces it, while this one only
    /// changes when the user picks again. Same scale as <see cref="Rating"/>
    /// (0 = G, 1 = R-18, 2 = R-18G). Optional, so version 2 files without it
    /// still load and no schema bump is needed.
    /// </summary>
    public int? UserRating { get; init; }

    /// <summary>The user's rating when there is one, otherwise the platform's.</summary>
    [JsonIgnore]
    public int EffectiveRating => UserRating ?? Rating;
}

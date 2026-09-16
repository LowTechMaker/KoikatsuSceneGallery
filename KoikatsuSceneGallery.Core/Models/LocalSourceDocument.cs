namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Describes one local source (a friend, or the user's own private stash).
/// Stored beside the source's card folder rather than in the app cache so it
/// travels with the library, mirroring <see cref="PostMetadataDocument"/>.
/// </summary>
/// <remarks>
/// <see cref="DisplayName"/> is authoritative for what the UI shows, while the
/// identity lives in <see cref="Id"/>, which is also encoded in the folder
/// name. Renaming a source therefore only rewrites this document; the folder,
/// and every card association derived from it, stays put.
/// </remarks>
internal sealed record LocalSourceDocument(
    int SchemaVersion,
    string Id,
    string DisplayName)
{
    public const int CurrentSchemaVersion = 1;

    /// <summary>Alternative spellings, matched by gallery search alongside the display name.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Free-form note, for example how and when the cards were received.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// Avatar file name relative to the metadata directory. A name rather than
    /// a path so the library stays portable across machines.
    /// </summary>
    public string? AvatarFileName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

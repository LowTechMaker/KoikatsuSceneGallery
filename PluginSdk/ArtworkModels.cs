namespace SceneGallery.PluginSdk;

/// <summary>Identity of an artwork at a provider, e.g. ("pixiv", "123456789").</summary>
public sealed record ArtworkId(string ProviderId, string Id);

/// <summary>Content rating from the provider. Values match pixiv's xRestrict field.</summary>
public enum ContentRating
{
    AllAges = 0,
    R18 = 1,
    R18G = 2,
}

/// <summary>A tag from the artwork, with optional translated name.</summary>
public sealed record ArtworkTag(string Name, string? TranslatedName);

/// <summary>
/// Artwork metadata as fetched from the provider. Carries the author identity
/// so the app can derive the destination folder without a separate user-profile
/// API call.
/// </summary>
public sealed record ArtworkInfo(
    ArtworkId ArtworkId,
    string AuthorName,
    string AuthorId,
    ContentRating Rating,
    IReadOnlyList<ArtworkTag> Tags,
    DateTimeOffset FetchedAt);

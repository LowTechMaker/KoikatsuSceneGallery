namespace SceneGallery.PluginSdk;

/// <summary>Identity of an author at a provider, e.g. ("pixiv", "12345678").</summary>
public sealed record AuthorKey(string ProviderId, string Id);

/// <summary>
/// Result of parsing a folder name. <see cref="FolderDisplayName"/> is the
/// human part of the folder name so the UI can show something immediately,
/// before (or instead of) a network fetch.
/// </summary>
public sealed record ParsedAuthor(AuthorKey Key, string FolderDisplayName);

/// <summary>
/// Author profile as fetched from the provider. <see cref="AvatarFilePath"/>
/// is an absolute local path to an already-downloaded image, or null when the
/// avatar could not be fetched.
/// </summary>
public sealed record AuthorInfo(
    AuthorKey Key,
    string Name,
    string? AvatarFilePath,
    string ProfileUrl,
    DateTimeOffset FetchedAt);

namespace SceneGallery.PluginSdk;

/// <summary>
/// A plugin that resolves artwork metadata from downloaded filenames, enabling
/// automated import of card files into the library. The app calls
/// <see cref="TryParseFilename"/> first (pure, no I/O), then
/// <see cref="FetchArtworkInfoAsync"/> for each recognized file.
/// </summary>
public interface ICardImportProvider : IPlugin
{
    /// <summary>Provider identifier, e.g. "pixiv" or "bepisdb".</summary>
    string ProviderId { get; }

    /// <summary>
    /// Extracts a provider-specific artwork identity from a filename.
    /// Pure string work, no I/O. Returns null when the filename doesn't
    /// match this provider's pattern.
    /// </summary>
    ArtworkId? TryParseFilename(string fileName);

    /// <summary>
    /// Fetches artwork metadata from the provider. Rate-limited internally.
    /// Returns null when the artwork cannot be resolved (deleted, private).
    /// </summary>
    Task<ArtworkInfo?> FetchArtworkInfoAsync(
        ArtworkId id,
        CancellationToken ct,
        bool saveToLocalCache = true);

    /// <summary>
    /// Extracts a provider-specific artwork identity from a subfolder name
    /// (e.g. "Title (123456789)"). Pure string work, no I/O.
    /// Returns null when the name doesn't match this provider's pattern.
    /// </summary>
    ArtworkId? TryParseArtworkFolderName(string folderName);

    /// <summary>Browser-openable URL for the artwork. No I/O.</summary>
    string GetArtworkUrl(ArtworkId id);
}

/// <summary>
/// Optional import destination hints. Providers that do not implement this keep
/// the default app layout: provider name folder plus rating folders.
/// </summary>
public interface IImportDestinationProvider
{
    /// <summary>Relative folder name inserted below the configured import subfolder.</summary>
    string DestinationFolderName { get; }

    /// <summary>Whether imports from this provider should be split into G/R-18/R-18G folders.</summary>
    bool UsesRatingFolders { get; }
}

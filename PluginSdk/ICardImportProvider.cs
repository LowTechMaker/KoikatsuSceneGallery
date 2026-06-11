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
    Task<ArtworkInfo?> FetchArtworkInfoAsync(ArtworkId id, CancellationToken ct);

    /// <summary>Browser-openable URL for the artwork. No I/O.</summary>
    string GetArtworkUrl(ArtworkId id);
}

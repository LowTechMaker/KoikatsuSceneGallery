namespace SceneGallery.PluginSdk;

/// <summary>
/// Result returned by a reverse image search provider. The provider may resolve
/// an artwork id directly, or only enough author/title metadata for import.
/// </summary>
public sealed record ReverseImageSearchResult(
    string SearchProvider,
    ArtworkId? ArtworkId,
    string? Title,
    string AuthorName,
    string AuthorId,
    double Similarity,
    string? ThumbnailUrl,
    string? SourceUrl);

/// <summary>
/// Optional plugin capability for resolving a local card image through a
/// reverse image search service.
/// </summary>
public interface IReverseImageSearchProvider : IPlugin
{
    Task<ReverseImageSearchResult?> SearchImageAsync(
        string imagePath,
        string apiKey,
        CancellationToken ct);
}

using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Finds the post sidecars belonging to cards already in memory.
/// </summary>
/// <remarks>
/// Shared by the tag cloud and the platform rating lookup, so both read the
/// same set of sidecars. Author folders are inferred from the cards rather than
/// by walking the library tree: a card sits either directly in its author
/// folder or one artwork subfolder down, so exactly two levels are probed.
/// </remarks>
internal static class PostSidecarLocator
{
    public static IReadOnlyList<string> AuthorDirectories(IEnumerable<CardBase> cards, IAppLogger logger)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            var folder = Path.GetDirectoryName(card.FilePath);
            if (string.IsNullOrEmpty(folder)) continue;
            candidates.Add(folder);
            if (Path.GetDirectoryName(folder) is { Length: > 0 } parent) candidates.Add(parent);
        }

        return [.. candidates.Where(directory => HasSidecars(directory, logger))];
    }

    /// <summary>Every readable sidecar under <paramref name="directories"/>; unreadable folders are logged and skipped.</summary>
    public static List<PostMetadataDocument> ReadAll(
        PostMetadataStore store, IReadOnlyList<string> directories, IAppLogger logger, string operation,
        CancellationToken token)
    {
        var documents = new List<PostMetadataDocument>();
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                documents.AddRange(store.ReadAll(directory));
            }
            catch (Exception ex)
            {
                logger.LogError(operation, ex, directory);
            }
        }
        return documents;
    }

    private static bool HasSidecars(string directory, IAppLogger logger)
    {
        try
        {
            return Directory.Exists(Path.Combine(
                directory, PostMetadataStore.MetadataDirectoryName, PostMetadataStore.FetchedDataDirectoryName));
        }
        catch (Exception ex)
        {
            logger.LogError("PostSidecars.ProbeDirectory", ex, directory);
            return false;
        }
    }
}

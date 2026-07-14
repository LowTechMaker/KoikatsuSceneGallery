using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

internal enum ImportFileConflict
{
    Move,
    Duplicate,
    Collision,
}

internal sealed record ImportPathOptions(
    string ImportSubfolder,
    string AuthorFolderFormat,
    string ArtworkFolderFormat);

internal static class ImportDestinationPolicy
{
    public static string FormatAuthorFolder(
        ImportPathOptions options,
        string authorName,
        string authorId)
        => PathSanitizer.SanitizeFolderName(options.AuthorFolderFormat
            .Replace("{name}", authorName)
            .Replace("{id}", authorId));

    public static string FormatArtworkFolder(
        ImportPathOptions options,
        string? title,
        string artworkId)
        => string.IsNullOrEmpty(title)
            ? $"({artworkId})"
            : PathSanitizer.SanitizeFolderName(options.ArtworkFolderFormat
                .Replace("{title}", title)
                .Replace("{id}", artworkId));

    public static string BuildTargetBase(
        string root,
        string importSubfolder,
        string providerFolder,
        string gameVersionFolder,
        string ratingFolder,
        string? authorFolder)
    {
        var parts = new List<string>(6) { root };
        AddIfPresent(parts, importSubfolder);
        AddIfPresent(parts, providerFolder);
        AddIfPresent(parts, gameVersionFolder);
        AddIfPresent(parts, ratingFolder);
        AddIfPresent(parts, authorFolder);
        return Path.Combine([.. parts]);
    }

    public static bool ShouldCreateArtworkFolder(
        bool alreadyExists,
        int threshold,
        int batchCount,
        bool? visualSimilarityVerdict)
    {
        if (alreadyExists)
            return true;

        var countExceeds = threshold >= 0 && batchCount > threshold;
        return visualSimilarityVerdict ?? countExceeds;
    }

    public static bool IsDuplicateFilename(
        IReadOnlySet<string> existingFilenames,
        string fileName)
        => existingFilenames.Contains(fileName);

    public static ImportFileConflict ClassifyFileConflict(
        bool destinationExists,
        bool filesAreIdentical)
        => !destinationExists
            ? ImportFileConflict.Move
            : filesAreIdentical
                ? ImportFileConflict.Duplicate
                : ImportFileConflict.Collision;

    public static IReadOnlyList<string> SelectRoots(
        CardType cardType,
        IReadOnlyList<string> sceneRoots,
        IReadOnlyList<string> characterRoots,
        IReadOnlyList<string> coordinateRoots)
        => cardType switch
        {
            CardType.Scene => sceneRoots,
            CardType.Character => characterRoots,
            CardType.Coordinate => coordinateRoots,
            _ => [],
        };

    private static void AddIfPresent(List<string> parts, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            parts.Add(value);
    }
}

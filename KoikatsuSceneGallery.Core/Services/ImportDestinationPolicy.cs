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

/// <summary>What <see cref="ImportDestinationPolicy.ResolveTarget"/> needs to know about one card.</summary>
/// <param name="Roots">The library roots for the card's type; the first is where new folders go.</param>
/// <param name="ProviderId">The post's provider, else the author's; null when neither is known.</param>
/// <param name="Scope">The card's provider folder, or <c>("", true)</c> when it has no provider.</param>
/// <param name="GameVersionFolder">The old layout's game folder for the card, to find folders made before the three-level layout. New folders have none.</param>
/// <param name="RatingFolder">The old layout's rating folder for the card, likewise; ignored when the scope has no rating folders.</param>
/// <param name="ProviderScopes">Every provider's scope, to find an author filed under another game version.</param>
/// <param name="FormatAuthorFolder">Turns (name, id) into the author's folder name, from the user's format.</param>
/// <param name="DirectoryExists">Whether a folder exists; <see cref="Directory.Exists"/> unless a test says otherwise.</param>
internal sealed record ImportTargetInput(
    IReadOnlyList<string> Roots,
    string ImportSubfolder,
    string UnknownFolderName,
    string GameVersionFolder,
    IReadOnlyList<string> GameVersionFolderNames,
    (string Folder, bool UsesRatingFolders) Scope,
    string RatingFolder,
    IReadOnlyList<(string Folder, bool UsesRatingFolders)> ProviderScopes,
    bool HasArtwork,
    string? ProviderId,
    string? AuthorId,
    string? AuthorName,
    string? AuthorProviderId,
    Func<string, string, string> FormatAuthorFolder,
    Func<string, bool>? DirectoryExists = null);

/// <param name="Folder">The folder the card goes in, before any artwork subfolder.</param>
/// <param name="AuthorDirectory">Where the post sidecar is written; null when the card has no post.</param>
internal sealed record ImportTarget(string Folder, string? AuthorDirectory);

internal static class ImportDestinationPolicy
{
    /// <summary>
    /// The folder a card goes to. Import and "give this card its source"
    /// both come here, so a change to the library layout is made once.
    /// </summary>
    /// <remarks>
    /// Null when there is no library root for the card's type. A card that
    /// ends up with no author folder at all goes to the Unknown folder's
    /// unrecognized sink: those cards are unrelated, and the sink is what
    /// makes <see cref="GalleryGrouping.GetKey"/> give each its own tile
    /// instead of folding every one of them into a single stack.
    /// </remarks>
    public static ImportTarget? ResolveTarget(
        ImportTargetInput input,
        Dictionary<string, Dictionary<string, string>> folderIndex)
    {
        var roots = input.Roots;
        if (roots.Count == 0) return null;

        var subfolder = input.ImportSubfolder.Trim();
        var scope = input.Scope;
        var ratingFolder = scope.UsesRatingFolders ? input.RatingFolder : "";
        var gameVersionFolder = input.GameVersionFolder;
        var directoryExists = input.DirectoryExists ?? Directory.Exists;
        string? targetFolder = null;
        var useUnrecognizedSubfolder = UsesUnrecognizedSink(
            hasArtwork: input.HasArtwork,
            hasAuthor: input.AuthorId is not null,
            input.AuthorProviderId);

        if (input.AuthorId is { } authorId)
        {
            // An author folder that exists is reused wherever it is: the new
            // layout first, then the old layout's own scope, then the same
            // rating under another game (the game no longer separates authors;
            // the rating still must, or an R-18 card would sit in a G folder).
            targetFolder = ImportAuthorDirectoryLookup.FindExact(roots, scope.Folder, "", "", authorId, folderIndex)
                ?? ImportAuthorDirectoryLookup.FindExact(
                    roots, scope.Folder, gameVersionFolder, ratingFolder, authorId, folderIndex)
                ?? input.GameVersionFolderNames
                    .Where(game => game != gameVersionFolder && (game.Length > 0 || ratingFolder.Length > 0))
                    .Select(game => ImportAuthorDirectoryLookup.FindExact(
                        roots, scope.Folder, game, ratingFolder, authorId, folderIndex))
                    .FirstOrDefault(found => found is not null);

            if (targetFolder is null && !input.HasArtwork)
            {
                var current = ImportAuthorDirectoryLookup.FindFallback(
                    roots, input.ProviderScopes, input.GameVersionFolderNames, "", "", authorId, folderIndex);
                var old = ImportAuthorDirectoryLookup.FindFallback(
                    roots, input.ProviderScopes,
                    input.GameVersionFolderNames, gameVersionFolder, ratingFolder, authorId, folderIndex);
                targetFolder = current.Directory ?? old.Directory;
                var matchedProvider = current.ProviderFolder ?? old.ProviderFolder;

                if (targetFolder is null && matchedProvider is not null && input.AuthorName is not null)
                {
                    var safeName = input.FormatAuthorFolder(input.AuthorName, authorId);
                    targetFolder = BuildTargetBase(roots[0], subfolder, matchedProvider, "", "", safeName);
                }
            }

            if (targetFolder is null && input.AuthorName is not null)
            {
                if (input.ProviderId is not null)
                {
                    var safeName = input.FormatAuthorFolder(input.AuthorName, authorId);
                    targetFolder = BuildTargetBase(roots[0], subfolder, scope.Folder, "", "", safeName);
                }
                else
                {
                    // Not in the folder index (no provider scope covers Unknown),
                    // so an old-layout folder is looked for on disk.
                    var folderName = PathSanitizer.SanitizeFolderName(authorId);
                    var current = BuildTargetBase(roots[0], subfolder, input.UnknownFolderName, "", "", folderName);
                    var old = BuildTargetBase(roots[0], subfolder, input.UnknownFolderName, gameVersionFolder, "", folderName);
                    targetFolder = !directoryExists(current) && directoryExists(old) ? old : current;
                    useUnrecognizedSubfolder = false;
                }
            }
        }

        if (targetFolder is null)
        {
            targetFolder = BuildTargetBase(roots[0], subfolder, input.UnknownFolderName, "", "", null);
            // No author folder: the card would sit loose in Unknown, where
            // every such card shares one gallery key.
            useUnrecognizedSubfolder = true;
        }

        var authorDirectory = input.HasArtwork ? targetFolder : null;
        if (useUnrecognizedSubfolder)
            targetFolder = Path.Combine(targetFolder, GalleryGrouping.UnrecognizedFolderName);
        return new ImportTarget(targetFolder, authorDirectory);
    }

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

    /// <summary>
    /// Whether an item belongs in the author folder's "unrecognized" sink,
    /// which collects files that have an author but no artwork identity.
    /// </summary>
    /// <remarks>
    /// Local sources never have an artwork identity by design, so they would
    /// otherwise all land in the sink. That would also cost them their folder
    /// grouping, because GalleryGrouping deliberately degrades that directory
    /// to per-file keys.
    /// </remarks>
    public static bool UsesUnrecognizedSink(
        bool hasArtwork,
        bool hasAuthor,
        string? authorProviderId)
        => !hasArtwork
            && hasAuthor
            && !LocalSourceIdentity.IsLocal(authorProviderId);

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

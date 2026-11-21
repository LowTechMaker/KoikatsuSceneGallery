using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>Why a card cannot be given a source.</summary>
internal enum ReidentifyProblem
{
    /// <summary>The card, or where it would go, is not inside a configured library root.</summary>
    NotInLibrary,
    /// <summary>Neither a post nor an author was given.</summary>
    NeedPostOrAuthor,
    /// <summary>A post was given but could not be fetched.</summary>
    PostNotFound,
}

/// <summary>What <see cref="LibraryCardRelocation.ApplyAsync"/> does around the move.</summary>
/// <param name="ComputeHash">The card's payload hash, or null when the file is not a card.</param>
/// <param name="WriteSidecar">Writes the post sidecar for the card at its new path; throws on failure.</param>
/// <param name="FollowCard">Re-points the user's rating and state at (new path, hash).</param>
/// <param name="LibraryChanged">(old path, new path): caches keyed by path are stale.</param>
/// <param name="FolderRatingOf">The rating a path's rating folders imply (<c>SceneRating.FromFolders</c>).</param>
/// <param name="FiledRatingOf">The rating already filed for the card at a path, or null.</param>
/// <param name="SetFiledRating">Records (path, hash, rating) as the card's filed rating; false when it could not.</param>
internal sealed record LibraryCardRelocationSteps(
    Func<string, CancellationToken, string?> ComputeHash,
    LibraryCardMover Mover,
    Func<string, CancellationToken, Task> WriteSidecar,
    Func<string, string, CancellationToken, Task> FollowCard,
    Action<string, string> LibraryChanged,
    Func<string, int> FolderRatingOf,
    Func<string, int?> FiledRatingOf,
    Func<string, string, int, CancellationToken, Task<bool>> SetFiledRating);

/// <summary>
/// Moves a library card to where its newly given source says it belongs, and
/// takes everything keyed by its path along. The decisions only; the app
/// supplies the post, the destination and the stores.
/// </summary>
/// <remarks>
/// The order matters. The hash is taken first, while the card is where the
/// stores last saw it, and a file without one is not moved at all: its rating
/// and state could not follow it. Nothing but the move happens unless the
/// card actually moved. Once it has, the rest — sidecar, rating and state,
/// caches — runs to the end even if the caller cancels, and a failure in any
/// of it never undoes the move: the card is in the right place, and a missing
/// sidecar is reported, not rolled back.
///
/// Ratings: a rating folder the card leaves may be all that says it is
/// R-18, so that rating is filed as card state, at the old path, before the
/// move; when it cannot be, the card does not move. A rating the user chose
/// is filed after the move and wins.
/// </remarks>
internal static class LibraryCardRelocation
{
    /// <summary>The <see cref="LibraryCardMoveResult.Error"/> of a move whose sidecar could not be written.</summary>
    public const string SidecarMissing = "sidecar-missing";

    /// <summary>Whether a card at <paramref name="filePath"/> can be given a source at all.</summary>
    public static ReidentifyProblem? Check(string filePath, IReadOnlyList<string> roots, bool hasPost, bool hasAuthor)
    {
        if (!IsInsideAny(filePath, roots)) return ReidentifyProblem.NotInLibrary;
        if (!hasPost && !hasAuthor) return ReidentifyProblem.NeedPostOrAuthor;
        return null;
    }

    /// <summary>The planned destination must be inside the library too; the rules make it so, this makes sure.</summary>
    public static ReidentifyProblem? CheckDestination(string destinationPath, IReadOnlyList<string> roots)
        => IsInsideAny(destinationPath, roots) ? null : ReidentifyProblem.NotInLibrary;

    private static bool IsInsideAny(string path, IReadOnlyList<string> roots)
    {
        var full = Path.GetFullPath(path);
        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).Any(root =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                PathComparison.Comparison));
    }

    /// <param name="chosenRating">The rating the user chose for the card, recorded after the move; null keeps what is there.</param>
    public static async Task<LibraryCardMoveResult> ApplyAsync(
        string sourcePath, string destinationPath, bool writesSidecar,
        LibraryCardRelocationSteps steps, CancellationToken ct, int? chosenRating = null)
    {
        ct.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourcePath);
        var hash = steps.ComputeHash(source, ct);
        if (hash is null)
            return new(LibraryCardMoveOutcome.Failed, source, "The file is not a card that can be identified.");

        // The rating folder the card is about to leave may be the only thing
        // that says it is R-18. That goes on disk first, at the path the card
        // still has; if it cannot, the card stays where its rating is visible.
        var implied = steps.FolderRatingOf(source);
        if (implied != SceneRating.General && steps.FiledRatingOf(source) is null)
        {
            bool recorded;
            try
            {
                recorded = await steps.SetFiledRating(source, hash, implied, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                recorded = false;
            }
            if (!recorded)
                return new(LibraryCardMoveOutcome.Failed, source, "The card's rating could not be recorded before moving it.");
        }

        var moved = await steps.Mover.MoveAsync(source, destinationPath, ct).ConfigureAwait(false);
        if (moved.Outcome != LibraryCardMoveOutcome.Moved)
            return moved;

        // Past this point the card has moved: finish, whatever the caller's token says.
        var newPath = moved.FinalPath;
        string? error = null;
        if (writesSidecar)
        {
            try
            {
                await steps.WriteSidecar(newPath, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                error = SidecarMissing;
            }
        }

        try
        {
            await steps.FollowCard(newPath, hash, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The stores re-attach a moved card by its hash on their own later
            // (CardStateReconciler); losing that here is not worth undoing the move.
        }

        if (chosenRating is { } rating)
        {
            try
            {
                await steps.SetFiledRating(newPath, hash, rating, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The card is in place; its rating can be set again from its menu.
            }
        }

        steps.LibraryChanged(source, newPath);
        return moved with { Error = error };
    }
}

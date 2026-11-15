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
internal sealed record LibraryCardRelocationSteps(
    Func<string, CancellationToken, string?> ComputeHash,
    LibraryCardMover Mover,
    Func<string, CancellationToken, Task> WriteSidecar,
    Func<string, string, CancellationToken, Task> FollowCard,
    Action<string, string> LibraryChanged);

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

    public static async Task<LibraryCardMoveResult> ApplyAsync(
        string sourcePath, string destinationPath, bool writesSidecar,
        LibraryCardRelocationSteps steps, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = Path.GetFullPath(sourcePath);
        var hash = steps.ComputeHash(source, ct);
        if (hash is null)
            return new(LibraryCardMoveOutcome.Failed, source, "The file is not a card that can be identified.");

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

        steps.LibraryChanged(source, newPath);
        return moved with { Error = error };
    }
}

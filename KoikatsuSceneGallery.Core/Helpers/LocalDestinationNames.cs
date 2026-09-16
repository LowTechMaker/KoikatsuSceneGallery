namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Picks the file name a staged local card lands under, so that a name already
/// taken by a different card does not abort the batch.
/// </summary>
/// <remarks>
/// The import transaction is all-or-nothing on purpose: a destination that
/// exists with different content fails the item and rolls the whole batch back.
/// That is right for an online import, where a name clash inside one artwork
/// folder means the plan was wrong. It is wrong here — friends' cards are named
/// by the game, two unrelated cards collide by accident, and one collision
/// among fifty-six leaves the user with nothing imported and no way to find the
/// offender.
///
/// So the clash is resolved before the plan is built, by suffixing the name.
/// Identical content is deliberately left alone: the executor recognizes it as
/// a duplicate, keeps the copy already in the library and deletes the source,
/// which is what the user wants and what renaming would destroy.
/// </remarks>
internal static class LocalDestinationNames
{
    /// <summary>Matches the executor's own rename ceiling.</summary>
    private const int MaxAttempts = 1000;

    /// <summary>
    /// The destination for <paramref name="sourcePath"/>, and the batch's
    /// claims updated with it.
    /// </summary>
    /// <param name="claimed">
    /// Destination path to the source file that claimed it, for the cards
    /// planned so far. Maps rather than a set so that two identical files in
    /// one batch can still be folded as duplicates.
    /// </param>
    /// <param name="exists">Whether a path is taken on disk.</param>
    /// <param name="identical">Whether two files have the same content.</param>
    public static string Resolve(
        string sourcePath,
        string idealDestinationPath,
        IDictionary<string, string> claimed,
        Func<string, bool> exists,
        Func<string, string, bool> identical)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(exists);
        ArgumentNullException.ThrowIfNull(identical);

        var directory = Path.GetDirectoryName(idealDestinationPath);
        var name = Path.GetFileNameWithoutExtension(idealDestinationPath);
        var extension = Path.GetExtension(idealDestinationPath);

        for (var attempt = 0; attempt <= MaxAttempts; attempt++)
        {
            var candidate = attempt == 0
                ? idealDestinationPath
                : Combine(directory, $"{name}_{attempt}{extension}");

            if (claimed.TryGetValue(candidate, out var claimant))
            {
                // Two copies of one card in the same batch. Planning both onto
                // the same destination is what lets the executor delete the
                // second source instead of importing it twice.
                if (identical(sourcePath, claimant))
                    return candidate;
                continue;
            }

            if (!exists(candidate) || identical(sourcePath, candidate))
            {
                claimed[candidate] = sourcePath;
                return candidate;
            }
        }

        // A thousand cards of that name already differ. Let the executor report
        // it rather than inventing a name nobody asked for.
        return idealDestinationPath;
    }

    private static string Combine(string? directory, string fileName)
        => string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
}

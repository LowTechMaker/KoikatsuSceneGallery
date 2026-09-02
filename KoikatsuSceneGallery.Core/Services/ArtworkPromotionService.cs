namespace KoikatsuSceneGallery.Services;

internal sealed record ArtworkPromotionResult(
    bool Succeeded,
    string? CollisionFileName)
{
    public static ArtworkPromotionResult Success { get; } = new(true, null);

    public static ArtworkPromotionResult Collision(string fileName)
        => new(false, fileName);
}

/// <summary>
/// Moves loose files for one artwork into its artwork directory only after all
/// filename/content collisions have been checked.  A failed move is rolled back.
/// </summary>
internal static class ArtworkPromotionService
{
    public static ArtworkPromotionResult PreflightAndPromote(
        IReadOnlyList<string> existingRootFiles,
        IReadOnlyList<string> incomingFiles,
        string artworkDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(existingRootFiles);
        ArgumentNullException.ThrowIfNull(incomingFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(artworkDirectory);

        var sources = existingRootFiles
            .Concat(incomingFiles)
            .Distinct(PathComparer)
            .ToList();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(source))
                throw new FileNotFoundException("A promotion source file no longer exists.", source);
        }

        foreach (var sameNameFiles in sources.GroupBy(
                     Path.GetFileName,
                     StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = sameNameFiles.Key
                ?? throw new InvalidDataException("A promotion source has no file name.");
            var destination = Path.Combine(artworkDirectory, fileName);
            var comparisonPath = File.Exists(destination)
                ? destination
                : sameNameFiles.First();

            foreach (var source in sameNameFiles)
            {
                if (PathComparer.Equals(source, comparisonPath))
                    continue;

                if (!ImportDuplicateDetector.AreFilesIdentical(
                        comparisonPath,
                        source,
                        cancellationToken))
                {
                    return ArtworkPromotionResult.Collision(fileName);
                }
            }
        }

        var artworkDirectoryExisted = Directory.Exists(artworkDirectory);
        Directory.CreateDirectory(artworkDirectory);

        var movedFiles = new List<(string Source, string Destination)>();
        var duplicateRootFiles = new List<string>();
        try
        {
            foreach (var source in existingRootFiles.Distinct(PathComparer))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(artworkDirectory, Path.GetFileName(source));
                if (File.Exists(destination))
                {
                    if (!ImportDuplicateDetector.AreFilesIdentical(
                            source,
                            destination,
                            cancellationToken))
                    {
                        throw new IOException(
                            $"The promotion destination changed after preflight: {destination}");
                    }
                    duplicateRootFiles.Add(source);
                    continue;
                }

                File.Move(source, destination);
                movedFiles.Add((source, destination));
            }
        }
        catch (Exception promotionException)
        {
            var rollbackExceptions = new List<Exception>();
            for (var i = movedFiles.Count - 1; i >= 0; i--)
            {
                var (source, destination) = movedFiles[i];
                try
                {
                    if (File.Exists(destination) && !File.Exists(source))
                        File.Move(destination, source);
                }
                catch (Exception rollbackException)
                {
                    rollbackExceptions.Add(rollbackException);
                }
            }

            if (rollbackExceptions.Count > 0)
            {
                throw new AggregateException(
                    "Artwork promotion failed and could not be fully rolled back.",
                    [promotionException, .. rollbackExceptions]);
            }

            if (!artworkDirectoryExisted
                && Directory.Exists(artworkDirectory)
                && !Directory.EnumerateFileSystemEntries(artworkDirectory).Any())
            {
                Directory.Delete(artworkDirectory);
            }

            throw;
        }

        foreach (var duplicate in duplicateRootFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(duplicate);
        }

        return ArtworkPromotionResult.Success;
    }

    private static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
}

namespace KoikatsuSceneGallery.Services;

/// <summary>Moves one local file into the folder representing an artwork.</summary>
internal sealed record ArtworkFileAssignment(string SourcePath, string DestinationDirectory);

/// <summary>
/// Performs a preflighted, rollback-capable batch move for manually assigned
/// local files. Existing destination files are never overwritten.
/// </summary>
internal sealed class ArtworkFileAssignmentService
{
    public void Move(IReadOnlyList<ArtworkFileAssignment> assignments, CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
            return;

        var plannedMoves = new List<(string Source, string Destination)>();
        var destinations = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        foreach (var assignment in assignments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(assignment.SourcePath);
            var directory = Path.GetFullPath(assignment.DestinationDirectory);
            if (!File.Exists(source))
                throw new FileNotFoundException("The selected local file no longer exists.", source);

            var destination = Path.Combine(directory, Path.GetFileName(source));
            if (string.Equals(source, destination,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                continue;
            }

            if (!destinations.Add(destination))
                throw new InvalidOperationException($"More than one selected file would use '{destination}'.");
            if (File.Exists(destination))
                throw new IOException($"Destination file already exists: {destination}");

            plannedMoves.Add((source, destination));
        }

        var createdDirectories = new List<string>();
        var completedMoves = new List<(string Source, string Destination)>();
        try
        {
            foreach (var directory in plannedMoves
                         .Select(static move => Path.GetDirectoryName(move.Destination)!)
                         .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    createdDirectories.Add(directory);
                }
            }

            foreach (var move in plannedMoves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(move.Source, move.Destination);
                completedMoves.Add(move);
            }
        }
        catch
        {
            foreach (var move in completedMoves.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(move.Destination) && !File.Exists(move.Source))
                        File.Move(move.Destination, move.Source);
                }
                catch
                {
                    // Preserve the original exception. The caller logs it and
                    // can surface the remaining path for manual recovery.
                }
            }

            foreach (var directory in createdDirectories.OrderByDescending(static path => path.Length))
            {
                try
                {
                    if (Directory.Exists(directory)
                        && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
                catch
                {
                    // Best-effort cleanup only; never remove non-empty folders.
                }
            }

            throw;
        }
    }
}

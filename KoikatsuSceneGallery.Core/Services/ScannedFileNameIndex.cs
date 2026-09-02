namespace KoikatsuSceneGallery.Services;

/// <summary>Result of matching a sidecar's recorded file names to one author directory.</summary>
internal readonly record struct ScannedFileNameMatch(
    IReadOnlyList<string> Files,
    bool HasAmbiguousName);

/// <summary>
/// Matches sidecar file names without guessing when several unowned local files
/// share a name. Files already attributed to a different artwork are never
/// claimed by this sidecar.
/// </summary>
internal sealed class ScannedFileNameIndex
{
    private readonly record struct Entry(string FilePath, string? OwnerArtworkId);

    private readonly Dictionary<string, List<Entry>> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(string filePath, string? ownerArtworkId)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
            return;

        if (!_entries.TryGetValue(fileName, out var entries))
        {
            entries = [];
            _entries[fileName] = entries;
        }

        entries.Add(new Entry(filePath, ownerArtworkId));
    }

    public ScannedFileNameMatch Match(string artworkId, IReadOnlyList<string> fileNames)
    {
        var files = new List<string>();
        var hasAmbiguousName = false;
        if (fileNames is null || fileNames.Count == 0)
            return new(files, hasAmbiguousName);

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || !_entries.TryGetValue(fileName, out var candidates))
            {
                continue;
            }

            var hasOwnedFile = false;
            Entry? singleUnownedFile = null;
            var unownedCount = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.OwnerArtworkId is null)
                {
                    unownedCount++;
                    singleUnownedFile ??= candidate;
                }
                else if (candidate.OwnerArtworkId.Equals(artworkId, StringComparison.OrdinalIgnoreCase))
                {
                    hasOwnedFile = true;
                    files.Add(candidate.FilePath);
                }
            }

            if (hasOwnedFile)
                continue;
            if (unownedCount == 1)
                files.Add(singleUnownedFile!.Value.FilePath);
            else if (unownedCount > 1)
                hasAmbiguousName = true;
        }

        return new(files, hasAmbiguousName);
    }
}

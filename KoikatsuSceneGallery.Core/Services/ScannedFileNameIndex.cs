namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The outcome of matching a sidecar's file names against a scanned author
/// directory. <paramref name="HasAmbiguousName"/> reports that a name could
/// have come from more than one eligible file, so nothing was claimed for it.
/// </summary>
/// <param name="Files">The files the sidecar may claim.</param>
/// <param name="HasAmbiguousName">
/// True when at least one name was left unresolved because several eligible
/// files share it.
/// </param>
internal readonly record struct ScannedFileNameMatch(
    IReadOnlyList<string> Files,
    bool HasAmbiguousName);

/// <summary>
/// Indexes the card files found under one author directory by file name so a
/// metadata sidecar can be matched to its local files.
///
/// Sidecars only store bare file names, which repeat across the per-artwork
/// subfolders of an author (001.png, 002.png, …). Every scanned file therefore
/// remembers the artwork it was found under, and a sidecar may only claim files
/// that no other artwork owns.
/// </summary>
internal sealed class ScannedFileNameIndex
{
    private readonly record struct Entry(string FilePath, string? OwnerArtworkId);

    private readonly Dictionary<string, List<Entry>> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Records a scanned file. <paramref name="ownerArtworkId"/> is the artwork
    /// the file already belongs to through its folder or file name, or null
    /// when nothing identified it.
    /// </summary>
    public void Record(string filePath, string? ownerArtworkId)
    {
        var fileName = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(fileName))
            return;

        if (!_entries.TryGetValue(fileName, out var list))
        {
            list = [];
            _entries[fileName] = list;
        }

        list.Add(new Entry(filePath, ownerArtworkId));
    }

    /// <summary>
    /// Resolves the sidecar's file names against the scan.
    ///
    /// A name resolves through ownership when files are already attributed to
    /// <paramref name="artworkId"/>. Otherwise only an unowned file can be
    /// claimed, and only when it is the single unowned candidate: a bare name
    /// shared by several unowned files identifies none of them, so the name is
    /// reported as ambiguous instead of guessed at.
    ///
    /// Files owned by a different artwork are never eligible and never make a
    /// name ambiguous — they belong to that other post.
    /// </summary>
    public ScannedFileNameMatch Match(
        string artworkId,
        IReadOnlyList<string> fileNames)
    {
        var files = new List<string>();
        var hasAmbiguousName = false;
        if (fileNames is null || fileNames.Count == 0)
            return new ScannedFileNameMatch(files, hasAmbiguousName);

        foreach (var fileName in fileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || !_entries.TryGetValue(fileName, out var candidates))
            {
                continue;
            }

            var ownedCount = 0;
            Entry? singleUnowned = null;
            var unownedCount = 0;
            foreach (var candidate in candidates)
            {
                if (candidate.OwnerArtworkId is null)
                {
                    unownedCount++;
                    singleUnowned ??= candidate;
                }
                else if (candidate.OwnerArtworkId.Equals(
                             artworkId,
                             StringComparison.OrdinalIgnoreCase))
                {
                    ownedCount++;
                    files.Add(candidate.FilePath);
                }
            }

            if (ownedCount > 0)
                continue;

            if (unownedCount == 1)
                files.Add(singleUnowned!.Value.FilePath);
            else if (unownedCount > 1)
                hasAmbiguousName = true;
        }

        return new ScannedFileNameMatch(files, hasAmbiguousName);
    }
}

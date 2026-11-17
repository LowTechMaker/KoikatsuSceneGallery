namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which cached thumbnails are no longer on disk, found by listing each
/// thumbnail folder once instead of asking about every file. The start of the
/// app used to call <see cref="File.Exists"/> for every cached card; a library
/// of thousands keeps its thumbnails in one flat folder, so one listing answers
/// the same question.
/// </summary>
/// <remarks>
/// Same answer as before for every thumbnail: a missing one is reported, so the
/// card starts without it and the thumbnail is made again when the card is
/// shown. A folder that cannot be listed counts as holding nothing.
/// </remarks>
internal static class ThumbnailPresence
{
    public sealed record Result(IReadOnlyList<string> Missing, int FoldersListed);

    /// <param name="thumbnails">Card key and its cached thumbnail path (null or empty: none).</param>
    /// <param name="listFolder">The full paths of the files in a folder, or null when it cannot be listed. Defaults to the disk.</param>
    public static Result Find(IEnumerable<(string Key, string? ThumbnailPath)> thumbnails,
        Func<string, IEnumerable<string>?>? listFolder = null)
    {
        listFolder ??= ListFromDisk;
        var folders = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var (key, thumbnail) in thumbnails)
        {
            if (string.IsNullOrEmpty(thumbnail)) continue;
            var folder = Path.GetDirectoryName(thumbnail) ?? "";
            if (!folders.TryGetValue(folder, out var names))
            {
                names = listFolder(folder) is { } files
                    ? new HashSet<string>(files.Select(Path.GetFileName).OfType<string>(), StringComparer.OrdinalIgnoreCase)
                    : null;
                folders[folder] = names;
            }
            if (names is null || !names.Contains(Path.GetFileName(thumbnail)))
                missing.Add(key);
        }
        return new Result(missing, folders.Count);
    }

    private static IEnumerable<string>? ListFromDisk(string folder)
    {
        try
        {
            return Directory.Exists(folder) ? Directory.GetFiles(folder) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

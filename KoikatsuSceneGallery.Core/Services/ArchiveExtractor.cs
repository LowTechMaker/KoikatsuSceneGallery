using System.IO.Compression;

namespace KoikatsuSceneGallery.Services;

/// <param name="Files">Extracted cards (.png) and mods (.zipmod, or .zip with a manifest), with the archive each came from.</param>
/// <param name="SkippedEntries">Entries left out: other file types, unsafe paths, or over the size limits.</param>
public sealed record ArchiveExtraction(IReadOnlyList<ExtractedFile> Files, int SkippedEntries);

/// <param name="ArchivePath">The archive the user dropped, even for a file that came out of an archive inside it.</param>
public sealed record ExtractedFile(string Path, string ArchivePath);

/// <summary>
/// Unpacks an archive that is not itself a mod into a staging folder, so that the cards
/// and mods inside it can join the import like loose files. The archive is only read.
/// </summary>
/// <remarks>
/// Only what the import can use comes out: PNG files, .zipmod files, and .zip files.
/// A .zip inside is kept whole when it is a mod (it has a manifest) and unpacked in turn
/// otherwise, one level deep. Entry paths are kept, so a pack's folders still group its
/// cards; a path that would leave the staging folder is skipped.
/// </remarks>
public static class ArchiveExtractor
{
    public const long MaxEntryBytes = 512L * 1024 * 1024;
    public const long MaxTotalBytes = 4L * 1024 * 1024 * 1024;
    public const int MaxEntries = 20_000;
    private const int MaxDepth = 2;

    public static bool IsCandidateArchive(string path)
        => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase);

    public static ArchiveExtraction Extract(string archivePath, string stagingDirectory, CancellationToken ct = default)
    {
        var files = new List<ExtractedFile>();
        var budget = new Budget();
        ExtractInto(archivePath, archivePath, stagingDirectory, depth: 1, files, budget, ct);
        return new ArchiveExtraction(files, budget.Skipped);
    }

    private sealed class Budget
    {
        public long Bytes;
        public int Entries;
        public int Skipped;
    }

    private static void ExtractInto(
        string archivePath, string originalArchive, string directory, int depth,
        List<ExtractedFile> files, Budget budget, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // a folder

            var extension = Path.GetExtension(entry.Name);
            var isCard = extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
            var isMod = extension.Equals(".zipmod", StringComparison.OrdinalIgnoreCase);
            var isZip = extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
            if (!isCard && !isMod && !isZip) { budget.Skipped++; continue; }

            if (++budget.Entries > MaxEntries || entry.Length > MaxEntryBytes || budget.Bytes + entry.Length > MaxTotalBytes)
            {
                budget.Skipped++;
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(directory, SafeRelativePath(entry.FullName)));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) { budget.Skipped++; continue; }

            target = Unique(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
            budget.Bytes += entry.Length;

            if (isZip && ZipmodManifestReader.TryRead(target) is null)
            {
                // Not a mod: an archive inside the archive. Unpacked beside itself, then dropped.
                if (depth < MaxDepth)
                {
                    var inner = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
                    try { ExtractInto(target, originalArchive, inner, depth + 1, files, budget, ct); }
                    catch (InvalidDataException) { budget.Skipped++; }
                }
                else budget.Skipped++;
                TryDelete(target);
                continue;
            }

            files.Add(new ExtractedFile(target, originalArchive));
        }
    }

    // Entry names come from whoever made the archive: no drive, no rooted path, no "..",
    // and no characters Windows will not put in a file name.
    internal static string SafeRelativePath(string entryName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var parts = entryName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => part is not "." and not "..")
            .Select(part => new string(part.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' '))
            .Where(part => part.Length > 0)
            .ToArray();
        return parts.Length == 0 ? "_" : Path.Combine(parts);
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

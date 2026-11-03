using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The library's PNG files by name, for asking "do I already have this file?".
/// A name matches without its Windows " (1)" suffix and without regard to case,
/// and a size, when given, must match too: same name and same size is what
/// "an identical file" means here, since the other side of the question is a
/// file the user does not have in hand.
/// </summary>
internal sealed class LibraryNameIndex
{
    private readonly Dictionary<string, List<string>> _pathsByName;

    private LibraryNameIndex(Dictionary<string, List<string>> pathsByName) => _pathsByName = pathsByName;

    internal static string KeyOf(string fileName) => CopySuffix.Normalize(fileName).ToUpperInvariant();

    /// <summary>Scans <paramref name="roots"/> recursively. A root that cannot be read is logged and skipped.</summary>
    internal static LibraryNameIndex Build(
        IEnumerable<string> roots,
        Action<string, Exception, string?> logError,
        CancellationToken ct)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var root in roots.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    var key = KeyOf(Path.GetFileName(file));
                    if (!index.TryGetValue(key, out var paths)) index[key] = paths = [];
                    paths.Add(file);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { logError("LibraryNameIndex.Scan", ex, root); }
        }
        return new LibraryNameIndex(index);
    }

    internal static LibraryNameIndex FromPaths(IEnumerable<string> paths)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var key = KeyOf(Path.GetFileName(path));
            if (!index.TryGetValue(key, out var list)) index[key] = list = [];
            list.Add(path);
        }
        return new LibraryNameIndex(index);
    }

    /// <summary>Whether the library holds a file of this name, and of this size when one is given.</summary>
    internal bool Has(string fileName, long? size)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !_pathsByName.TryGetValue(KeyOf(fileName), out var paths))
            return false;
        if (size is null) return true;

        foreach (var path in paths)
        {
            try { if (new FileInfo(path).Length == size) return true; }
            catch (IOException) { /* gone since the scan */ }
            catch (UnauthorizedAccessException) { /* unreadable */ }
        }
        return false;
    }
}

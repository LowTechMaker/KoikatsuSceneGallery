namespace KoikatsuSceneGallery.Helpers;

internal static class PathSanitizer
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    public static string SanitizeFolderName(string name)
    {
        foreach (var c in InvalidChars)
            name = name.Replace(c, '_');
        return name.Trim();
    }

    public static string SanitizeRelativePath(string relativePath)
    {
        var parts = relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeFolderName)
            .Where(p => p.Length > 0)
            .ToArray();

        return parts.Length == 0 ? "" : Path.Combine(parts);
    }
}

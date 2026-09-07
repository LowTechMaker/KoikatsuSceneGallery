namespace KoikatsuSceneGallery.Helpers;

public static class GallerySearch
{
    // Each comma-separated keyword must match, but may match a different field.
    public static bool Matches(string filePath, string? authorName, IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
        {
            if (!filePath.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                && !(authorName?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                return false;
        }
        return true;
    }
}

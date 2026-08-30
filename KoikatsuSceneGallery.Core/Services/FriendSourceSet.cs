namespace KoikatsuSceneGallery.Services;

internal static class FriendSourceSet
{
    private const string RootPrefix = "root:";
    private const string FilePrefix = "file:";

    public static HashSet<string> Build(
        IEnumerable<string> roots,
        IEnumerable<string> linkedFilePaths)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(linkedFilePaths);

        var collapsedRoots = FriendFolderLayout.CollapseNestedRoots(roots);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in collapsedRoots)
            result.Add(RootPrefix + root);

        foreach (var path in linkedFilePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var fullPath = Path.GetFullPath(path);
            if (!collapsedRoots.Any(root =>
                    FriendFolderLayout.IsWithin(fullPath, root)))
            {
                result.Add(FilePrefix + fullPath);
            }
        }

        return result;
    }
}

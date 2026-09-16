namespace KoikatsuSceneGallery.Services;

internal static class LibraryMetadataDirectory
{
    public const string Name = ".scenegallery";

    // Callers create the directory and retain responsibility for handling errors.
    public static void MarkHiddenOnWindows(string path)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, attributes | FileAttributes.Hidden);
    }
}

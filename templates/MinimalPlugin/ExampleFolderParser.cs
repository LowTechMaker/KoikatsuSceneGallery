namespace SceneGallery.Plugin.Example;

internal static class ExampleFolderParser
{
    internal static string? ParseId(string folderName)
    {
        const string prefix = "example_";
        if (!folderName.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var id = folderName[prefix.Length..];
        return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
    }
}

namespace SceneGallery.PluginCommon;

internal static class AtomicFileWriter
{
    internal static void Write(string path, Action<Stream> serialize)
    {
        var tempPath = path + ".tmp";
        using (var stream = File.Create(tempPath))
            serialize(stream);
        File.Move(tempPath, path, overwrite: true);
    }
}

namespace KoikatsuSceneGallery.Services;

internal static class JpegCacheFile
{
    public static bool IsComplete(string filePath)
    {
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (stream.Length < 4)
                return false;

            var first = stream.ReadByte();
            var second = stream.ReadByte();
            stream.Seek(-2, SeekOrigin.End);
            var penultimate = stream.ReadByte();
            var last = stream.ReadByte();

            return first == 0xFF
                   && second == 0xD8
                   && penultimate == 0xFF
                   && last == 0xD9;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

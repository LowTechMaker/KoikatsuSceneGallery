using System.Buffers.Binary;

namespace KoikatsuSceneGallery.Helpers;

public static class PngHelper
{
    public static (int Width, int Height) ReadDimensions(string filePath)
    {
        Span<byte> header = stackalloc byte[24];
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Read(header) < 24)
            return (0, 0);
        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..])
        );
    }
}

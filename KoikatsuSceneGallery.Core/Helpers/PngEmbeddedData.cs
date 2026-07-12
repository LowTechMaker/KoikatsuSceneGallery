using System.Buffers.Binary;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Locates the binary data appended after the PNG image in a Koikatsu scene
/// card. Ported from jim60105/PluginDataReader (PngFile.GetPngSize).
/// </summary>
public static class PngEmbeddedData
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Returns the byte length of the leading PNG image — i.e. the offset where
    /// the appended studio data begins — or 0 if the stream is not a PNG. Only
    /// seeks past chunk headers (never reads pixel data), so it is cheap even
    /// for large images. Leaves the stream position unchanged.
    /// </summary>
    public static long GetPngSize(Stream stream)
    {
        long start = stream.Position;
        try
        {
            Span<byte> sig = stackalloc byte[8];
            if (stream.Read(sig) < 8 || !sig.SequenceEqual(PngSignature))
                return 0;

            Span<byte> lenBuf = stackalloc byte[4];
            Span<byte> typeBuf = stackalloc byte[4];
            while (true)
            {
                if (stream.Read(lenBuf) < 4) return 0;
                int dataLen = BinaryPrimitives.ReadInt32BigEndian(lenBuf);
                if (dataLen < 0) return 0;
                if (stream.Read(typeBuf) < 4) return 0;
                bool isIend = typeBuf.SequenceEqual("IEND"u8);
                // chunk data + 4-byte CRC must fit in the remaining stream
                if ((long)dataLen + 4 > stream.Length - stream.Position) return 0;
                stream.Seek((long)dataLen + 4, SeekOrigin.Current);
                if (isIend) return stream.Position - start;
            }
        }
        catch (Exception)
        {
            return 0;
        }
        finally
        {
            stream.Seek(start, SeekOrigin.Begin);
        }
    }
}

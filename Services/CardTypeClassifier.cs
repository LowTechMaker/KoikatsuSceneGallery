using System.Text;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

public enum CardType
{
    NotACard,
    Scene,
    Character,
    Coordinate,
}

/// <summary>
/// Classifies a PNG file as a Koikatsu scene, character, or coordinate card
/// by reading the binary markers appended after the PNG IEND chunk. Opens the
/// file once and runs a cascading check — no full metadata is parsed.
/// </summary>
public static class CardTypeClassifier
{
    private const string CharacterMarker = "KoiKatuChara";
    private const string CoordinateMarker = "KoiKatuClothes";
    private static readonly byte[] StudioToken = Encoding.UTF8.GetBytes("【KStudio】");

    public static CardType Classify(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0 || pngSize >= fs.Length)
                return CardType.NotACard;

            fs.Seek(pngSize, SeekOrigin.Begin);

            // Character and coordinate cards start with: int productNo + string marker.
            // Scene cards have raw studio data; the marker read will either fail or
            // produce a string that doesn't match any known card marker.
            if (TryReadMarker(fs, out var marker))
            {
                if (marker.Contains(CoordinateMarker, StringComparison.Ordinal))
                    return CardType.Coordinate;
                if (marker.Contains(CharacterMarker, StringComparison.Ordinal))
                    return CardType.Character;
            }

            // Not a chara/coord card — scan for the studio token (scene card).
            fs.Seek(pngSize, SeekOrigin.Begin);
            if (ScanForStudioToken(fs))
                return CardType.Scene;

            return CardType.NotACard;
        }
        catch (Exception)
        {
            return CardType.NotACard;
        }
    }

    /// <summary>
    /// Reads productNo (int32) + marker (BinaryReader length-prefixed string)
    /// from the current stream position. Returns false if the data doesn't
    /// look like a valid BinaryReader string (length out of range, not enough
    /// bytes, non-ASCII control characters).
    /// </summary>
    private static bool TryReadMarker(Stream stream, out string marker)
    {
        marker = string.Empty;
        try
        {
            using var br = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            _ = br.ReadInt32(); // productNo
            marker = br.ReadString();
            return marker.Length > 0 && marker.Length < 256;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Scans the stream from the current position for the 【KStudio】 byte
    /// sequence. Uses 1 MB chunks with overlap (same approach as
    /// SceneMetadataParser). Returns true if found.
    /// </summary>
    private static bool ScanForStudioToken(Stream stream)
    {
        const int chunkSize = 1 << 20; // 1 MB
        int overlap = StudioToken.Length - 1;
        byte[] buffer = new byte[chunkSize + overlap];
        int carried = 0;

        while (true)
        {
            int read = stream.Read(buffer, carried, chunkSize);
            int available = carried + read;
            if (available < StudioToken.Length)
                return false;

            if (buffer.AsSpan(0, available).IndexOf(StudioToken) >= 0)
                return true;

            if (read <= 0)
                return false;

            int keep = Math.Min(overlap, available);
            buffer.AsSpan(available - keep, keep).CopyTo(buffer);
            carried = keep;
        }
    }
}

using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

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
    private const string CharacterMarker        = "KoiKatuChara";
    private const string SunshineCharacterMarker = "KoiKatuCharaSun";
    private const string CoordinateMarker        = "KoiKatuClothes";
    private const string SunshineCoordinateMarker = "KoiKatuClothesSun";

    private static readonly byte[] StudioToken   = Encoding.UTF8.GetBytes("【KStudio】");
    // KKS scene files embed KKS character data; scanning for this token is a
    // best-effort heuristic — empty KKS scenes (props only) may be mis-classified.
    private static readonly byte[] SunshineToken = Encoding.UTF8.GetBytes("KoiKatuCharaSun");

    /// <summary>Legacy entry point — returns card type only.</summary>
    public static CardType Classify(string filePath)
        => ClassifyExtended(filePath).cardType;

    /// <summary>Returns both card type and detected game version.</summary>
    public static (CardType cardType, GameVersion gameVersion) ClassifyExtended(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0 || pngSize >= fs.Length)
                return (CardType.NotACard, GameVersion.Unknown);

            fs.Seek(pngSize, SeekOrigin.Begin);

            // Character and coordinate cards start with: int productNo + string marker.
            // Check KKS markers BEFORE KK ones because KKS markers start with the KK
            // marker as a prefix (e.g. "KoiKatuCharaSun" contains "KoiKatuChara").
            if (TryReadMarker(fs, out var marker))
            {
                if (marker.Contains(SunshineCoordinateMarker, StringComparison.Ordinal))
                    return (CardType.Coordinate, GameVersion.KoikatsuSunshine);
                if (marker.Contains(CoordinateMarker, StringComparison.Ordinal))
                    return (CardType.Coordinate, GameVersion.Koikatsu);
                if (marker.Contains(SunshineCharacterMarker, StringComparison.Ordinal))
                    return (CardType.Character, GameVersion.KoikatsuSunshine);
                if (marker.Contains(CharacterMarker, StringComparison.Ordinal))
                    return (CardType.Character, GameVersion.Koikatsu);
            }

            // Not a chara/coord card — scan for the studio token (scene card).
            // Also scan for the KKS character token embedded in scene data.
            fs.Seek(pngSize, SeekOrigin.Begin);
            if (ScanForStudioAndSunshine(fs, out bool hasSunshine))
                return (CardType.Scene, hasSunshine ? GameVersion.KoikatsuSunshine : GameVersion.Koikatsu);

            return (CardType.NotACard, GameVersion.Unknown);
        }
        catch (Exception)
        {
            return (CardType.NotACard, GameVersion.Unknown);
        }
    }

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
    /// Scans from the current position for 【KStudio】. Simultaneously checks
    /// for the KKS sunshine token to distinguish KK vs KKS scenes.
    /// </summary>
    private static bool ScanForStudioAndSunshine(Stream stream, out bool hasSunshine)
    {
        hasSunshine = false;
        const int chunkSize = 1 << 20;
        int overlap = Math.Max(StudioToken.Length, SunshineToken.Length) - 1;
        byte[] buffer = new byte[chunkSize + overlap];
        int carried = 0;
        bool foundStudio = false;

        while (true)
        {
            int read = stream.Read(buffer, carried, chunkSize);
            int available = carried + read;
            if (available == 0) return foundStudio;

            var span = buffer.AsSpan(0, available);
            if (!foundStudio && span.IndexOf(StudioToken) >= 0)
                foundStudio = true;
            if (!hasSunshine && span.IndexOf(SunshineToken) >= 0)
                hasSunshine = true;

            if (read <= 0) return foundStudio;

            int keep = Math.Min(overlap, available);
            buffer.AsSpan(available - keep, keep).CopyTo(buffer);
            carried = keep;
        }
    }
}

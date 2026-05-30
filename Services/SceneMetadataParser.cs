using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using MessagePack;

namespace KoikatsuSceneGallery.Services;

/// <summary>The raw data extracted from a scene PNG before classification.</summary>
public readonly record struct ParsedScene(IReadOnlyList<string> PluginGuids, GameVersion Game);

/// <summary>
/// Extracts the embedded ExtendedSave plugin GUIDs and the game version from a
/// Koikatsu studio scene PNG. Parsing is ported from jim60105/PluginDataReader,
/// but only the plugin-map keys are read (values are skipped via
/// <see cref="MessagePackReader.Skip()"/>), so even very large embedded blobs
/// (e.g. 58 MB VNGE scenes) are traversed without being materialized.
///
/// The game version is detected in the same single pass over the appended data:
/// scenes embed their characters, whose card marker is game-specific —
/// 【KoiKatuCharaSun】 for Koikatsu Sunshine, 【KoiKatuChara】(/SP) for Koikatsu.
/// </summary>
public static class SceneMetadataParser
{
    private const string ExtendedSaveMarker = "KKEx";
    // Studio scenes mark the start of their data with this token; the
    // ExtendedSave block (marker + version + length + MessagePack map) follows it.
    private static readonly byte[] StudioToken = Encoding.UTF8.GetBytes("【KStudio】");
    private static readonly byte[] CharaMarker = "KoiKatuChara"u8.ToArray();
    private static readonly byte[] SunshineSuffix = "Sun"u8.ToArray();

    /// <summary>
    /// Parses a scene file, or returns null if it has no recognizable studio
    /// ExtendedSave data (not a scene, or no plugin data). Never throws.
    /// </summary>
    public static ParsedScene? TryParse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0) return null;
            fs.Seek(pngSize, SeekOrigin.Begin);

            var (afterStudioToken, game) = ScanAppended(fs);
            if (afterStudioToken < 0) return null;
            fs.Seek(afterStudioToken, SeekOrigin.Begin);

            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
            string marker = br.ReadString();
            _ = br.ReadInt32(); // data version
            int length = br.ReadInt32();
            if (marker != ExtendedSaveMarker || length <= 0) return null;
            if (length > fs.Length - fs.Position) return null;

            byte[] blob = br.ReadBytes(length);
            if (blob.Length != length) return null;
            return new ParsedScene(ReadTopLevelKeys(blob), game);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Single sequential pass over the appended data (1 MB chunks with overlap):
    /// locates the studio token and, before reaching it, infers the game version
    /// from the first embedded character-card marker. Returns the absolute
    /// position just after the studio token (or -1 if not found) and the game.
    /// </summary>
    private static (long AfterStudioToken, GameVersion Game) ScanAppended(Stream stream)
    {
        const int chunkSize = 1 << 20; // 1 MB
        // Overlap must cover the longest needle, including the "Sun" suffix we
        // peek at after the chara marker, so a match never straddles a boundary.
        int overlap = Math.Max(StudioToken.Length, CharaMarker.Length + SunshineSuffix.Length) - 1;
        byte[] buffer = new byte[chunkSize + overlap];
        long windowStart = stream.Position;
        int carried = 0;
        var game = GameVersion.Unknown;

        while (true)
        {
            int read = stream.Read(buffer, carried, chunkSize);
            int available = carried + read;
            var span = buffer.AsSpan(0, available);

            if (game == GameVersion.Unknown)
                game = DetectGame(span);

            if (available >= StudioToken.Length)
            {
                int idx = span.IndexOf(StudioToken);
                if (idx >= 0)
                    return (windowStart + idx + StudioToken.Length, game);
            }
            if (read <= 0) return (-1, game);

            int keep = Math.Min(overlap, available);
            buffer.AsSpan(available - keep, keep).CopyTo(buffer);
            windowStart += available - keep;
            carried = keep;
        }
    }

    private static GameVersion DetectGame(ReadOnlySpan<byte> span)
    {
        int s = span.IndexOf(CharaMarker);
        if (s < 0) return GameVersion.Unknown;
        // Need the 3 bytes after the marker to tell Sunshine ("Sun") from base.
        // If they're cut off at the chunk end, defer — the overlap re-presents
        // this marker with full context on the next chunk.
        int suffixStart = s + CharaMarker.Length;
        if (suffixStart + SunshineSuffix.Length > span.Length) return GameVersion.Unknown;
        return span.Slice(suffixStart, SunshineSuffix.Length).SequenceEqual(SunshineSuffix)
            ? GameVersion.KoikatsuSunshine
            : GameVersion.Koikatsu;
    }

    private static List<string> ReadTopLevelKeys(byte[] blob)
    {
        var reader = new MessagePackReader(blob);
        int count = reader.ReadMapHeader();
        var keys = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            keys.Add(reader.ReadString() ?? string.Empty);
            reader.Skip(); // skip the plugin's value without materializing it
        }
        return keys;
    }
}

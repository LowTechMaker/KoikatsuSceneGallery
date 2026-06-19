using System.Text;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public readonly record struct ParsedScene(GameVersion Game);

public static class SceneMetadataParser
{
    private static readonly byte[] StudioToken = Encoding.UTF8.GetBytes("【KStudio】");
    private static readonly byte[] CharaMarker = "KoiKatuChara"u8.ToArray();
    private static readonly byte[] SunshineSuffix = "Sun"u8.ToArray();

    public static ParsedScene? TryParse(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);

            long pngSize = PngEmbeddedData.GetPngSize(fs);
            if (pngSize <= 0) return null;
            fs.Seek(pngSize, SeekOrigin.Begin);

            var (foundStudio, game) = ScanAppended(fs);
            if (!foundStudio) return null;
            return new ParsedScene(game);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (bool FoundStudio, GameVersion Game) ScanAppended(Stream stream)
    {
        const int chunkSize = 1 << 20;
        int overlap = Math.Max(StudioToken.Length, CharaMarker.Length + SunshineSuffix.Length) - 1;
        byte[] buffer = new byte[chunkSize + overlap];
        int carried = 0;
        var game = GameVersion.Unknown;

        while (true)
        {
            int read = stream.Read(buffer, carried, chunkSize);
            int available = carried + read;
            var span = buffer.AsSpan(0, available);

            if (game == GameVersion.Unknown)
                game = DetectGame(span);

            if (available >= StudioToken.Length && span.IndexOf(StudioToken) >= 0)
                return (true, game);

            if (read <= 0) return (false, game);

            int keep = Math.Min(overlap, available);
            buffer.AsSpan(available - keep, keep).CopyTo(buffer);
            carried = keep;
        }
    }

    private static GameVersion DetectGame(ReadOnlySpan<byte> span)
    {
        int s = span.IndexOf(CharaMarker);
        if (s < 0) return GameVersion.Unknown;
        int suffixStart = s + CharaMarker.Length;
        if (suffixStart + SunshineSuffix.Length > span.Length) return GameVersion.Unknown;
        return span.Slice(suffixStart, SunshineSuffix.Length).SequenceEqual(SunshineSuffix)
            ? GameVersion.KoikatsuSunshine
            : GameVersion.Koikatsu;
    }
}

using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Helpers;

public record FilenameLinkInfo(
    string? PixivArtworkId,
    string? PixivUrl,
    string? BepisDbId,
    string? BepisDbUrl);

public static partial class FilenameLinkParser
{
    [GeneratedRegex(@"(\d{6,})_p\d+")]
    private static partial Regex PixivIdPattern();

    [GeneratedRegex(@"(KKSCENE|KKCLOTHING|KK)_(\d+)")]
    private static partial Regex BepisDbPattern();

    private static readonly Dictionary<string, string> BepisDbPrefixMap = new()
    {
        ["KKSCENE"] = "kkscenes",
        ["KKCLOTHING"] = "kkclothing",
        ["KK"] = "koikatsu",
    };

    public static readonly FilenameLinkInfo Empty = new(null, null, null, null);

    public static FilenameLinkInfo Parse(string? filePath)
    {
        if (filePath is null) return Empty;
        var name = Path.GetFileNameWithoutExtension(filePath);

        var bepisMatch = BepisDbPattern().Match(name);
        string? bepisId = null, bepisUrl = null;
        if (bepisMatch.Success && BepisDbPrefixMap.TryGetValue(bepisMatch.Groups[1].Value, out var category))
        {
            bepisId = bepisMatch.Groups[0].Value;
            var id = long.Parse(bepisMatch.Groups[2].Value);
            bepisUrl = $"https://db.bepis.moe/{category}/view/{id}";
        }

        string? pixivId = null, pixivUrl = null;
        var pixivMatch = PixivIdPattern().Match(name);
        if (pixivMatch.Success)
        {
            var overlapsBepisDb = bepisMatch.Success
                && pixivMatch.Index < bepisMatch.Index + bepisMatch.Length
                && pixivMatch.Index + pixivMatch.Length > bepisMatch.Index;
            if (!overlapsBepisDb)
            {
                pixivId = pixivMatch.Groups[1].Value;
                pixivUrl = $"https://www.pixiv.net/artworks/{pixivId}";
            }
        }

        return new(pixivId, pixivUrl, bepisId, bepisUrl);
    }
}

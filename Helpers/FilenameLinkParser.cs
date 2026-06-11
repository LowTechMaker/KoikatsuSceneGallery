using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Helpers;

public record FilenameLinkInfo(
    string? PixivArtworkId,
    string? PixivUrl,
    string? BepisDbId,
    string? BepisDbUrl);

public static class FilenameLinkParser
{
    private static readonly Regex PixivIdPattern = new(@"(\d{6,})_p\d+", RegexOptions.Compiled);
    private static readonly Regex BepisDbPattern = new(@"(KKSCENE|KKCLOTHING|KK)_(\d+)", RegexOptions.Compiled);

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

        var pixivMatch = PixivIdPattern.Match(name);
        var pixivId = pixivMatch.Success ? pixivMatch.Groups[1].Value : null;
        var pixivUrl = pixivId != null ? $"https://www.pixiv.net/artworks/{pixivId}" : null;

        var bepisMatch = BepisDbPattern.Match(name);
        string? bepisId = null, bepisUrl = null;
        if (bepisMatch.Success)
        {
            bepisId = bepisMatch.Groups[0].Value;
            var category = BepisDbPrefixMap[bepisMatch.Groups[1].Value];
            var id = long.Parse(bepisMatch.Groups[2].Value);
            bepisUrl = $"https://db.bepis.moe/{category}/view/{id}";
        }

        return new(pixivId, pixivUrl, bepisId, bepisUrl);
    }
}

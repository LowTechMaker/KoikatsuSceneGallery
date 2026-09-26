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

    /// <summary>
    /// A BepisDB id in the canonical form the site and the plugin use, with the
    /// file name's zero padding removed: <c>KKSCENE_078928</c> becomes
    /// <c>KKSCENE_78928</c>.
    /// </summary>
    /// <remarks>
    /// The displayed id keeps whatever the file name had, but anything used as
    /// an identity has to agree with the plugin's own parser — otherwise the
    /// same card gets one identity with the plugin installed and another
    /// without it, and neither matches the id stored in the artwork sidecars.
    /// </remarks>
    public static string CanonicalBepisDbId(string bepisDbId)
    {
        var separator = bepisDbId.LastIndexOf('_');
        if (separator < 0) return bepisDbId;
        var digits = bepisDbId[(separator + 1)..].TrimStart('0');
        if (digits.Length == 0) digits = "0";
        return string.Concat(bepisDbId.AsSpan(0, separator + 1), digits);
    }

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

using System.Net;
using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Pulls the links out of a post description. Platforms hand back descriptions
/// as HTML or as text with bare URLs; pixiv additionally wraps links as
/// <c>/jump.php?https%3A%2F%2F...</c>, with the real address percent-encoded.
/// </summary>
public static partial class PostLinkExtractor
{
    [GeneratedRegex(@"/jump\.php\?(?<url>[^""'\s<>]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JumpPattern();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    private const string TrailingPunctuation = ".,;:!?)]}>、。，）」』】";

    /// <summary>Distinct absolute http(s) links, in the order they first appear. Never null.</summary>
    public static IReadOnlyList<string> Extract(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return [];

        var text = WebUtility.HtmlDecode(description);
        var found = new List<(int Index, string Url)>();

        foreach (Match match in JumpPattern().Matches(text))
        {
            var decoded = TryUnescape(match.Groups["url"].Value);
            if (decoded is not null && IsHttp(decoded))
                found.Add((match.Index, Clean(decoded)));
        }
        foreach (Match match in UrlPattern().Matches(text))
            found.Add((match.Index, Clean(match.Value)));

        return found
            .OrderBy(entry => entry.Index)
            .Select(entry => entry.Url)
            .Where(url => url.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string Clean(string url) => url.TrimEnd(TrailingPunctuation.ToCharArray());

    private static bool IsHttp(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
           || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static string? TryUnescape(string value)
    {
        try { return Uri.UnescapeDataString(value); }
        catch (UriFormatException) { return null; }
    }
}

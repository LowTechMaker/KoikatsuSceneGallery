using System.Net;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace KoikatsuSceneGallery.Helpers;

internal static partial class HtmlDescriptionRenderer
{
    private sealed record TextStyle(
        Brush? Foreground = null,
        FontWeight? FontWeight = null,
        FontStyle? FontStyle = null,
        TextDecorations? TextDecorations = null);

    public static void Render(RichTextBlock target, string? html)
    {
        target.Blocks.Clear();
        var paragraph = new Paragraph();
        target.Blocks.Add(paragraph);

        if (string.IsNullOrWhiteSpace(html))
            return;

        var text = html.Replace("\r\n", "\n").Replace('\r', '\n');
        var position = 0;
        var styles = new Stack<TextStyle>();
        styles.Push(new TextStyle());

        foreach (Match match in TagPattern().Matches(text))
        {
            AppendText(paragraph, text[position..match.Index], styles.Peek());

            var tag = match.Value;
            if (IsLineBreak(tag) || IsBlockBoundary(tag))
            {
                AppendLineBreak(paragraph);
            }
            else if (TryParseAnchor(tag, out var href)
                     && TryFindAnchorClose(text, match.Index + match.Length, out var closeIndex))
            {
                var contentStart = match.Index + match.Length;
                var rawText = text[contentStart..closeIndex];
                AppendLinkOrText(paragraph, href, StripTags(rawText), styles.Peek());
                position = closeIndex + AnchorCloseTagLength;
                continue;
            }
            else if (TryEnterStyle(tag, styles.Peek(), out var nextStyle))
            {
                styles.Push(nextStyle);
            }
            else if (IsStyleCloseTag(tag) && styles.Count > 1)
            {
                styles.Pop();
            }

            position = match.Index + match.Length;
        }

        AppendText(paragraph, text[position..], styles.Peek());
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    [GeneratedRegex("""href\s*=\s*(?:"(?<url>[^"]*)"|'(?<url>[^']*)'|(?<url>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HrefPattern();

    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex AnyTagPattern();

    [GeneratedRegex("""style\s*=\s*(?:"(?<style>[^"]*)"|'(?<style>[^']*)'|(?<style>[^\s>]+))""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StyleAttributePattern();

    [GeneratedRegex(@"color\s*:\s*(?<color>#[0-9a-fA-F]{6})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ColorStylePattern();

    private const int AnchorCloseTagLength = 4;

    private static void AppendText(Paragraph paragraph, string rawText, TextStyle style)
    {
        if (rawText.Length == 0) return;

        var decoded = WebUtility.HtmlDecode(rawText);
        if (decoded.Length == 0) return;

        var parts = decoded.Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                AppendLineBreak(paragraph);
            if (parts[i].Length > 0)
                paragraph.Inlines.Add(ApplyStyle(new Run { Text = parts[i] }, style));
        }
    }

    private static void AppendLinkOrText(Paragraph paragraph, string href, string rawText, TextStyle style)
    {
        var decodedText = WebUtility.HtmlDecode(rawText);
        if (!TryNormalizeUri(WebUtility.HtmlDecode(href), out var uri))
        {
            AppendText(paragraph, decodedText, style);
            return;
        }

        var link = new Hyperlink { NavigateUri = uri };
        link.Inlines.Add(ApplyStyle(
            new Run { Text = string.IsNullOrWhiteSpace(decodedText) ? uri.ToString() : decodedText },
            style));
        paragraph.Inlines.Add(link);
    }

    private static void AppendLineBreak(Paragraph paragraph)
    {
        if (paragraph.Inlines.LastOrDefault() is LineBreak)
            return;
        paragraph.Inlines.Add(new LineBreak());
    }

    private static bool IsLineBreak(string tag)
        => tag.StartsWith("<br", StringComparison.OrdinalIgnoreCase);

    private static bool IsBlockBoundary(string tag)
        => tag.StartsWith("</p", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("<p", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</div", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("<div", StringComparison.OrdinalIgnoreCase);

    private static bool IsStyleCloseTag(string tag)
        => tag.StartsWith("</span", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</strong", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</b", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</i", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</em", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</s", StringComparison.OrdinalIgnoreCase)
           || tag.StartsWith("</strike", StringComparison.OrdinalIgnoreCase);

    private static bool TryEnterStyle(string tag, TextStyle current, out TextStyle next)
    {
        next = current;
        if (tag.StartsWith("<strong", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("<b", StringComparison.OrdinalIgnoreCase))
        {
            next = current with { FontWeight = FontWeights.Bold };
            return true;
        }

        if (tag.StartsWith("<i", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("<em", StringComparison.OrdinalIgnoreCase))
        {
            next = current with { FontStyle = FontStyle.Italic };
            return true;
        }

        if (tag.StartsWith("<span", StringComparison.OrdinalIgnoreCase)
            && TryParseSpanColor(tag, out var color))
        {
            next = current with { Foreground = new SolidColorBrush(color) };
            return true;
        }

        if (tag.StartsWith("<s", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("<strike", StringComparison.OrdinalIgnoreCase))
        {
            next = current with { TextDecorations = TextDecorations.Strikethrough };
            return true;
        }

        return false;
    }

    private static bool TryParseAnchor(string tag, out string href)
    {
        href = "";
        if (!tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
            return false;

        var hrefMatch = HrefPattern().Match(tag);
        if (!hrefMatch.Success)
            return false;

        href = hrefMatch.Groups["url"].Value;
        return true;
    }

    private static bool TryFindAnchorClose(string text, int contentStart, out int closeIndex)
    {
        closeIndex = text.IndexOf("</a>", contentStart, StringComparison.OrdinalIgnoreCase);
        return closeIndex >= contentStart;
    }

    private static string StripTags(string value)
        => AnyTagPattern().Replace(value, "");

    private static bool TryNormalizeUri(string href, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(href))
            return false;

        var trimmed = href.Trim();
        const string pixivJump = "/jump.php?";
        var jumpIndex = trimmed.IndexOf(pixivJump, StringComparison.OrdinalIgnoreCase);
        if (jumpIndex >= 0)
            trimmed = Uri.UnescapeDataString(trimmed[(jumpIndex + pixivJump.Length)..]);

        return Uri.TryCreate(trimmed, UriKind.Absolute, out uri!)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static Run ApplyStyle(Run run, TextStyle style)
    {
        if (style.Foreground is not null)
            run.Foreground = style.Foreground;
        if (style.FontWeight is not null)
            run.FontWeight = style.FontWeight.Value;
        if (style.FontStyle is not null)
            run.FontStyle = style.FontStyle.Value;
        if (style.TextDecorations is not null)
            run.TextDecorations = style.TextDecorations.Value;
        return run;
    }

    private static bool TryParseSpanColor(string tag, out Color color)
    {
        color = default;

        var styleMatch = StyleAttributePattern().Match(tag);
        if (!styleMatch.Success)
            return false;

        var colorMatch = ColorStylePattern().Match(WebUtility.HtmlDecode(styleMatch.Groups["style"].Value));
        if (!colorMatch.Success)
            return false;

        var value = colorMatch.Groups["color"].Value;
        color = Color.FromArgb(
            255,
            Convert.ToByte(value.Substring(1, 2), 16),
            Convert.ToByte(value.Substring(3, 2), 16),
            Convert.ToByte(value.Substring(5, 2), 16));
        return true;
    }
}

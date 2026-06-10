using System.Text.RegularExpressions;
using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.PixivAuthors;

/// <summary>
/// Extracts a pixiv user id from folder names like "ArtistName (12345678)" or
/// "ArtistName_12345678". Pure string work, safe on hot scan paths.
/// </summary>
public static partial class PixivFolderNameParser
{
    public const string ProviderId = "pixiv";

    // "Name (12345678)" — also fullwidth parens and square brackets. Old pixiv
    // accounts have ids as short as 4 digits; the explicit delimiters make
    // short numbers safe to accept here.
    [GeneratedRegex(@"^(?<name>.+?)\s*[\(（\[]\s*(?<id>\d{4,12})\s*[\)）\]]$", RegexOptions.CultureInvariant)]
    private static partial Regex BracketedForm();

    // "Name_12345678" / "Name-12345678" / "Name 12345678". A bare trailing
    // number is ambiguous ("Scenes 2024"), so this form requires 6+ digits —
    // a judgment call trading missed short ids for fewer false positives.
    [GeneratedRegex(@"^(?<name>.+?)[ _\-]+(?<id>\d{6,12})$", RegexOptions.CultureInvariant)]
    private static partial Regex SuffixForm();

    public static ParsedAuthor? TryParse(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return null;

        var match = BracketedForm().Match(folderName);
        if (!match.Success)
            match = SuffixForm().Match(folderName);
        if (!match.Success) return null;

        var id = match.Groups["id"].Value.TrimStart('0');
        if (id.Length == 0) return null;

        var name = match.Groups["name"].Value.Trim();
        if (name.Length == 0) name = id;

        return new ParsedAuthor(new AuthorKey(ProviderId, id), name);
    }
}

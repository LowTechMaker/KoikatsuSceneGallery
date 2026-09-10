using System.Security.Cryptography;
using System.Text.RegularExpressions;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

/// <summary>
/// Identity rules for locally collected cards: files that are not publicly
/// shared, or that a friend passed on privately. A local source is an author
/// whose identity lives entirely in the folder name, so nothing about it is
/// ever fetched from a remote provider.
/// </summary>
/// <remarks>
/// Parsing is deliberately strict. AuthorInfoService falls back to trying every
/// provider when a directory sits outside all provider scopes, so a loose
/// pattern here is the one way a remote author folder could be claimed as
/// local. The mandatory "local-" prefix keeps this pattern disjoint from
/// providers whose ids are numeric.
/// </remarks>
internal static partial class LocalSourceIdentity
{
    /// <summary>Provider id shared by the built-in local source provider and every local author key.</summary>
    public const string ProviderId = "local";

    /// <summary>Prefix every local author id carries, in the key and in the folder name alike.</summary>
    public const string FolderIdPrefix = "local-";

    private const int SlugLength = 6;

    // Crockford-style alphabet: no vowels to avoid accidental words, no 0/1/l/o
    // to avoid transcription mistakes when a user retypes a folder name.
    private const string SlugAlphabet = "23456789bcdfghjkmnpqrstvwxz";

    [GeneratedRegex(
        @"^(?<name>.*?)\s*\((?<id>local-[A-Za-z0-9_-]{3,32})\)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex FolderPattern { get; }

    [GeneratedRegex(
        @"^local-[A-Za-z0-9_-]{3,32}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern { get; }

    /// <summary>Whether a provider id denotes a local source.</summary>
    public static bool IsLocal(string? providerId)
        => string.Equals(providerId, ProviderId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether an author id is a well-formed local source id.</summary>
    public static bool IsValidId(string? id)
        => id is not null && IdPattern.IsMatch(id);

    /// <summary>Generates a fresh local source id, prefix included.</summary>
    public static string NewId()
    {
        var slug = string.Create(SlugLength, 0, static (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = SlugAlphabet[RandomNumberGenerator.GetInt32(SlugAlphabet.Length)];
        });

        return FolderIdPrefix + slug;
    }

    /// <summary>
    /// Normalizes a user-supplied slug into a local source id. Returns null when
    /// the slug cannot form a valid id.
    /// </summary>
    public static string? FormatFolderId(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var trimmed = slug.Trim();
        var id = trimmed.StartsWith(FolderIdPrefix, StringComparison.OrdinalIgnoreCase)
            ? FolderIdPrefix + trimmed[FolderIdPrefix.Length..]
            : FolderIdPrefix + trimmed;

        return IsValidId(id) ? id : null;
    }

    /// <summary>
    /// The folder name this source's folder should take after a rename, or
    /// null when the rename cannot be expressed as a folder name.
    /// </summary>
    /// <remarks>
    /// Derived from the existing folder name rather than from the configured
    /// author-folder format, so the shape the folder was actually created with
    /// survives a settings change, and so a rename can never move a folder out
    /// of the pattern <see cref="TryParseFolderId"/> recognizes — which is the
    /// only thing tying the folder to its cards.
    ///
    /// Null when the old name does not parse, when it already carries the new
    /// name, or when sanitizing the new name leaves nothing: a folder called
    /// only "(local-xxxx)" is worse than one whose label is stale.
    /// </remarks>
    public static string? RenameFolderName(string? oldFolderName, string? newDisplayName)
    {
        if (!TryParseFolderId(oldFolderName, out var id, out var oldDisplayName))
            return null;

        var sanitized = PathSanitizer.SanitizeFolderName(newDisplayName?.Trim() ?? "").Trim();
        if (sanitized.Length == 0 || string.Equals(sanitized, oldDisplayName, StringComparison.Ordinal))
            return null;

        return $"{sanitized} ({id})";
    }

    /// <summary>
    /// Extracts a local source identity from a single directory name. Pure
    /// string work, no I/O, so it is safe on hot scan paths.
    /// </summary>
    public static bool TryParseFolderId(
        string? folderName,
        out string id,
        out string displayName)
    {
        id = "";
        displayName = "";

        if (string.IsNullOrWhiteSpace(folderName))
            return false;

        var match = FolderPattern.Match(folderName.Trim());
        if (!match.Success)
            return false;

        id = match.Groups["id"].Value;
        displayName = match.Groups["name"].Value.Trim();
        return true;
    }
}

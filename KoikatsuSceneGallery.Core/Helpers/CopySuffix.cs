using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Strips the suffix a file system or browser adds when it cannot overwrite a
/// file, so a copy can be recognized as a copy of something.
/// </summary>
/// <remarks>
/// Only ever used to widen the set of files worth comparing. Nothing is decided
/// on the strength of a normalized name — the comparison that follows reads the
/// bytes — so a false match here costs one extra read, never a wrong verdict.
/// That is what allows the pattern to be as loose as it is.
/// </remarks>
internal static partial class CopySuffix
{
    /// <summary>
    /// One trailing copy marker: " (2)", "(2)", "_2", " - Copy", " - 複製",
    /// " - 副本", " - 复制", " - コピー", optionally followed by a number.
    /// </summary>
    [GeneratedRegex(
        @"(?:\s*\((?<n>\d{1,4})\)|_(?<n2>\d{1,4})|\s*-\s*(?:Copy|copy|複製|复制|副本|コピー)(?:\s*\(\d{1,4}\))?)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Marker { get; }

    /// <summary>
    /// <paramref name="fileName"/> with every trailing copy marker removed,
    /// extension kept. Returns the name unchanged when it carries none.
    /// </summary>
    public static string Normalize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        // Repeated, because a file copied twice collects two markers, as in
        // "card - Copy (2)".
        while (true)
        {
            var trimmed = Marker.Replace(name, string.Empty, 1);
            if (trimmed.Length == 0 || string.Equals(trimmed, name, StringComparison.Ordinal))
                break;

            name = trimmed;
        }

        return name.TrimEnd() + extension;
    }

    /// <summary>
    /// Whether two file names differ only by a copy marker. False when they are
    /// the same name, which the caller already handles.
    /// </summary>
    public static bool AreCopiesOfOneName(string? left, string? right)
        => !string.Equals(left, right, StringComparison.OrdinalIgnoreCase)
            && Normalize(left).Length > 0
            && string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}

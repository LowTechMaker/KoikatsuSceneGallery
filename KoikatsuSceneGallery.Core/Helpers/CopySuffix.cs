using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Strips the suffix a file system or browser adds when it cannot overwrite a
/// file, so a copy can be recognized as a copy of something.
/// </summary>
/// <remarks>
/// Deliberately narrow, and deliberately not applied repeatedly. A normalized
/// name only widens the set of files whose bytes are then compared, so a false
/// match costs a read rather than a wrong verdict — but it costs a read per
/// candidate, and a pattern that collapses many names into one makes that set
/// the whole library.
///
/// That is not hypothetical. An earlier version treated a trailing "_123" as a
/// marker and stripped markers in a loop, so a Koikatsu scene name like
/// "2026_0910_1547_51_330.png" collapsed all the way down to "2026.png" — one
/// bucket holding every scene card in the library, every one of them hashed on
/// a single card's import. Hence: no numeric underscore suffix (Windows does
/// not produce one; only this app's own destination naming does), and one
/// marker at most.
/// </remarks>
internal static partial class CopySuffix
{
    /// <summary>
    /// One trailing copy marker: " (2)", "(2)", " - Copy", " - 複製",
    /// " - 复制", " - 副本", " - コピー", the words optionally followed by a
    /// number.
    /// </summary>
    [GeneratedRegex(
        @"(?:\s*\((?<n>\d{1,4})\)|\s*-\s*(?:Copy|copy|複製|复制|副本|コピー)(?:\s*\(\d{1,4}\))?)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex Marker { get; }

    /// <summary>
    /// <paramref name="fileName"/> with one trailing copy marker removed,
    /// extension kept. Returns the name unchanged when it carries none.
    /// </summary>
    public static string Normalize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return string.Empty;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var trimmed = Marker.Replace(name, string.Empty, 1).TrimEnd();

        return (trimmed.Length == 0 ? name : trimmed) + extension;
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

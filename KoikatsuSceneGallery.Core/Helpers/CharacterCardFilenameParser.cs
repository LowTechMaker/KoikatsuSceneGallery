using System.Text.RegularExpressions;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Extracts the creation timestamp embedded in Koikatsu character-card filenames.
/// Pattern: <c>Koikatu_[FM]_YYYYMMDDHHmmssfff_CharacterName.png</c>
/// </summary>
public static partial class CharacterCardFilenameParser
{
    [GeneratedRegex(@"Koikatu_[FM]_(\d{17})_")]
    private static partial Regex TimestampRegex();

    public static DateTime? ParseTimestamp(string fileName)
    {
        var match = TimestampRegex().Match(fileName);
        if (!match.Success) return null;

        return DateTime.TryParseExact(
            match.Groups[1].Value,
            "yyyyMMddHHmmssfff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var result) ? result : null;
    }
}

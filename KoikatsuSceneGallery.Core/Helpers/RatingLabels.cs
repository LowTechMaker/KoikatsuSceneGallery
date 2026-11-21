namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How the three content ratings are written on screen. They are the
/// platforms' own names, the same in every language, so they are not in the
/// resw files; they live here so they are spelt once (they were copied into
/// about fifteen places: badges, menus, combo boxes, the title bar's filter).
/// </summary>
/// <remarks>
/// Properties, not constants, so XAML can read them with
/// <c>{x:Bind helpers:RatingLabels.R18}</c>. Not the stored values: config and
/// filters keep their own tokens ("R18", "AllAges"), and the default folder
/// names are folder names that happen to match.
/// </remarks>
public static class RatingLabels
{
    public static string General => "G";
    public static string R18 => "R-18";
    public static string R18G => "R-18G";

    /// <summary>In order: general, R-18, R-18G (the index is the rating's value in the rating pickers).</summary>
    public static IReadOnlyList<string> All { get; } = [General, R18, R18G];

    /// <summary>Whether a label is one of the two restricted ratings (drawn in the warning colour).</summary>
    public static bool IsRestricted(string? label) => label == R18 || label == R18G;
}

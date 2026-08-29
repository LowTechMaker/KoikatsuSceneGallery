using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Ordering and visibility rules for a character's version group. They live
/// outside the view model so the grouping semantics can be covered by tests.
/// </summary>
internal static class CharacterVersionRules
{
    /// <summary>
    /// Orders a version group: current variants first, newest first within a
    /// variant kind. The head of a sorted group is its representative.
    /// </summary>
    public static int CompareVersions(
        CharacterVariantKind firstKind,
        DateTime firstTimestamp,
        CharacterVariantKind secondKind,
        DateTime secondTimestamp)
    {
        var kind = firstKind.CompareTo(secondKind);
        return kind != 0 ? kind : secondTimestamp.CompareTo(firstTimestamp);
    }

    /// <summary>
    /// Decides whether a card belongs in the gallery for the current query.
    /// </summary>
    public static bool IsVisible(
        CharacterVariantKind kind,
        bool isLatestVersion,
        bool isGroupRepresentative,
        bool hasSearchQuery,
        bool includeAlternatives,
        bool includeOldVersions)
        => kind switch
        {
            CharacterVariantKind.Current => isLatestVersion,

            // A character whose copies all sit under #old_version has no
            // current variant, so no card of it is the latest version.
            // Showing the group's representative keeps the character from
            // disappearing from the gallery altogether.
            CharacterVariantKind.OldVersion =>
                isGroupRepresentative || (hasSearchQuery && includeOldVersions),

            CharacterVariantKind.Alternative =>
                hasSearchQuery && includeAlternatives,

            _ => false,
        };
}

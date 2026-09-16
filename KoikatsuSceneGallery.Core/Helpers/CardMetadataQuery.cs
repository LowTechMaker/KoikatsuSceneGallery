using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

public static class CardMetadataQuery
{
    /// <summary>
    /// Whether every keyword appears somewhere in the card's text.
    /// </summary>
    /// <param name="note">
    /// The user's own note, when the card type has one. Optional so callers
    /// without notes are unaffected.
    /// </param>
    public static bool MatchesText(CardMetadataSummary? summary, string path, string? author, string? name,
        IReadOnlyList<string> keywords, string? note = null)
    {
        foreach (string keyword in keywords)
        {
            bool Contains(string? text) => text?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
            if (!(Contains(path) || Contains(author) || Contains(name) || Contains(summary?.Name)
                || Contains(summary?.Nickname) || Contains(summary?.UserId) || Contains(summary?.DataId)
                || Contains(note)
                || summary?.PluginGuids.Any(Contains) == true)) return false;
        }
        return true;
    }

    /// <summary>Whether every keyword appears in the note alone.</summary>
    public static bool NoteMatches(string? note, IReadOnlyList<string> keywords)
    {
        if (string.IsNullOrWhiteSpace(note) || keywords.Count == 0) return false;
        foreach (string keyword in keywords)
        {
            if (!note.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public static bool PassesFilters(CardMetadataSummary? summary, int? sex, int? personality, string? pluginGuid)
    {
        if (sex is null && personality is null && string.IsNullOrEmpty(pluginGuid)) return true;
        if (summary is null) return false;
        return (sex is null || summary.Sex == sex)
            && (personality is null || (summary.PersonalityId ?? -1) == personality)
            && (string.IsNullOrEmpty(pluginGuid) || summary.PluginGuids.Contains(pluginGuid, StringComparer.OrdinalIgnoreCase));
    }
}

using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Orders the cards of one character and picks the one that represents it in
/// the gallery.
/// </summary>
/// <remarks>
/// Generic over the card so this can live in Core and be tested directly. The
/// gallery view model that owns the real version index needs a dispatcher, so
/// without this seam the rule below would have no tests at all.
/// </remarks>
public static class CharacterVersionRanker
{
    /// <summary>
    /// Sorts <paramref name="group"/> newest first and returns the index of the
    /// card that represents the character, or -1 when the group is empty.
    /// </summary>
    /// <remarks>
    /// A what-if version never represents the character, however new it is —
    /// otherwise adding one would push the original out of the gallery. But it
    /// must not be able to leave the character with no representative either,
    /// so the choice falls back twice:
    /// <list type="number">
    /// <item>the newest card that is current and not superseded;</item>
    /// <item>failing that, the newest card that is not superseded — so a
    /// character whose only live cards are what-ifs still appears;</item>
    /// <item>failing that, simply the newest — so a character whose every card
    /// has been marked superseded still appears.</item>
    /// </list>
    /// </remarks>
    public static int SortAndFindPrimary<T>(
        List<T> group,
        Func<T, DateTime> timestamp,
        Func<T, CharacterVersionKind> kind,
        Func<T, bool> superseded)
    {
        ArgumentNullException.ThrowIfNull(group);

        if (group.Count == 0)
            return -1;

        group.Sort((a, b) => timestamp(b).CompareTo(timestamp(a)));

        for (var i = 0; i < group.Count; i++)
        {
            if (!superseded(group[i]) && kind(group[i]) == CharacterVersionKind.Current)
                return i;
        }

        for (var i = 0; i < group.Count; i++)
        {
            if (!superseded(group[i]))
                return i;
        }

        return 0;
    }

    /// <summary>
    /// Whether a what-if version counts as one the user still cares about.
    /// </summary>
    /// <remarks>
    /// Used for the gallery badge that tells you a character has variants.
    /// A what-if the user marked as replaced is deliberately excluded: they
    /// already said they were done with it, so advertising it would be noise.
    ///
    /// This is the only rule a label still drives besides which card
    /// represents the character. Labels do not decide what the gallery shows —
    /// one character occupies one tile, whatever its versions are marked as.
    /// </remarks>
    public static bool CountsAsLiveAlternate(CharacterVersionKind kind, bool superseded)
        => kind == CharacterVersionKind.Alternate && !superseded;
}

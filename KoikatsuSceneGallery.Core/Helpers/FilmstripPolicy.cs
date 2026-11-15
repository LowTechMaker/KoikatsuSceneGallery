namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which neighbours the detail pages' filmstrip shows, and which of them it
/// darkens. Pure, so the page only builds tiles.
/// </summary>
public static class FilmstripPolicy
{
    /// <summary>
    /// A run of at most <c>radius * 2 + 1</c> cards around <paramref name="index"/>,
    /// slid inwards at either end of the list so the strip stays full.
    /// </summary>
    public static (int Start, int Count) Window(int total, int index, int radius)
    {
        if (total <= 0 || index < 0 || index >= total || radius < 0) return (0, 0);
        var count = Math.Min(total, radius * 2 + 1);
        var start = Math.Clamp(index - radius, 0, total - count);
        return (start, count);
    }

    /// <summary>
    /// Darkens every card outside the current card's folder or post, so the
    /// edges of that folder stand out. A card that shares its group with no
    /// other card on the strip is on its own, and then nothing is darkened.
    /// </summary>
    public static bool[] Dimmed(IReadOnlyList<string> groupKeys, int current)
    {
        var dimmed = new bool[groupKeys.Count];
        if (current < 0 || current >= groupKeys.Count) return dimmed;

        var key = groupKeys[current];
        var shared = false;
        for (var i = 0; i < groupKeys.Count && !shared; i++)
            shared = i != current && Same(groupKeys[i], key);
        if (!shared) return dimmed;

        for (var i = 0; i < groupKeys.Count; i++)
            dimmed[i] = !Same(groupKeys[i], key);
        return dimmed;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

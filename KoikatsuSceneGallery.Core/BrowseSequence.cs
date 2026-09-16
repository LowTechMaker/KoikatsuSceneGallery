namespace KoikatsuSceneGallery.Helpers;

public static class BrowseSequence
{
    /// <summary>Keep the original browsing order after deletion; prefer the next surviving image.</summary>
    public static T? AfterRemoval<T>(IReadOnlyList<T> original, IReadOnlyList<T> live, string removed, Func<T, string> key) where T : class
    {
        var remaining = live.ToDictionary(key, StringComparer.OrdinalIgnoreCase);
        int index = -1;
        for (int i = 0; i < original.Count; i++) if (key(original[i]).Equals(removed, StringComparison.OrdinalIgnoreCase)) { index = i; break; }
        if (index < 0) return live.FirstOrDefault();
        for (int i = index + 1; i < original.Count; i++) if (remaining.TryGetValue(key(original[i]), out var next)) return next;
        for (int i = index - 1; i >= 0; i--) if (remaining.TryGetValue(key(original[i]), out var previous)) return previous;
        return null;
    }
}

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which translation of a tag, if any, the reader should see.
/// </summary>
/// <remarks>
/// A Chinese reader is better served by the original tag than by an English
/// stand-in: pixiv's tags are Japanese, mostly written in kanji a Chinese reader
/// can read, and where they are not, the Japanese is still the tag itself —
/// searchable, and what the artwork page shows. So the order is: a translation
/// in the reader's language, then the original. English comes first only for an
/// English reader.
///
/// This is decided by what the translation is written in, not by where it came
/// from, because the platform does not say. Asked for Traditional Chinese,
/// pixiv answers in the same field with English when it has no Chinese — 原神
/// comes back as "Genshin Impact" — and the translations saved at import were
/// English for every language. Judging the text itself covers all of them,
/// including sidecars already on disk.
/// </remarks>
public static class TagTranslationPolicy
{
    /// <summary>
    /// The first of <paramref name="candidates"/> that is really in
    /// <paramref name="uiLanguage"/>, or null when none is and the original
    /// tag should be shown.
    /// </summary>
    /// <param name="uiLanguage">BCP-47 tag of the UI, e.g. "zh-Hant".</param>
    /// <param name="name">The original tag; a translation equal to it adds nothing.</param>
    public static string? Pick(string? uiLanguage, string name, params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var text = candidate.Trim();
            if (string.Equals(text, name, StringComparison.Ordinal)) continue;
            if (IsInLanguage(text, uiLanguage)) return text;
        }
        return null;
    }

    /// <summary>What the reader sees for the tag: its usable translation, else the tag.</summary>
    public static string Label(string? uiLanguage, string name, params string?[] candidates)
        => Pick(uiLanguage, name, candidates) ?? name;

    /// <summary>
    /// Whether <paramref name="text"/> can be a translation into
    /// <paramref name="uiLanguage"/>.
    /// </summary>
    /// <remarks>
    /// Only Chinese is held to its script: a Chinese translation has Han
    /// characters, an English stand-in has none. Other languages accept what
    /// they are given — an English reader is exactly who the English is for.
    /// </remarks>
    public static bool IsInLanguage(string text, string? uiLanguage)
        => !IsChinese(uiLanguage) || text.Any(IsHan);

    private static bool IsChinese(string? uiLanguage)
        => uiLanguage is not null && uiLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static bool IsHan(char c)
        => c is >= '一' and <= '鿿'   // CJK Unified Ideographs
            or >= '㐀' and <= '䶿'    // Extension A
            or >= '豈' and <= '﫿';   // Compatibility Ideographs
}

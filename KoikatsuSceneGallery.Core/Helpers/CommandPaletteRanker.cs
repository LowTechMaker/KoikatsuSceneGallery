namespace KoikatsuSceneGallery.Helpers;

/// <summary>What a command palette result is; also its order when scores tie.</summary>
public enum PaletteCategory
{
    Command = 0,
    Setting = 1,
    Author = 2,
    Card = 3,
}

/// <summary>One thing the palette can find.</summary>
/// <param name="Title">What is shown and matched first.</param>
/// <param name="Keywords">
/// Other words that should find it - a setting's description, an English name
/// for a command - matched more weakly than the title.
/// </param>
public sealed record PaletteCandidate<T>(
    string Title, IReadOnlyList<string> Keywords, PaletteCategory Category, T Payload);

/// <summary>
/// Orders palette results for a query.
/// </summary>
/// <remarks>
/// <para>Every whitespace-separated term has to appear somewhere, so typing
/// more narrows the list instead of widening it. Within that, a title that
/// starts with the term beats one that merely contains it, and the title beats
/// the keywords: "篩選" should put the filter settings above a card that
/// happens to have the word in a long description.</para>
/// <para>Each category keeps a few places, so a library with thousands of
/// matching file names cannot push every command and setting off the
/// list.</para>
/// </remarks>
public static class CommandPaletteRanker
{
    private const int Exact = 100, Prefix = 80, WordStart = 60, Contains = 40,
        KeywordPrefix = 30, KeywordContains = 20;

    /// <summary>
    /// The score of <paramref name="title"/> and <paramref name="keywords"/>
    /// for <paramref name="query"/>, or 0 when some term matches nothing.
    /// </summary>
    public static int Score(string query, string title, IEnumerable<string> keywords)
    {
        var terms = Terms(query);
        if (terms.Length == 0) return 0;
        var words = keywords as IReadOnlyCollection<string> ?? keywords.ToArray();
        var total = 0;
        foreach (var term in terms)
        {
            var best = TermScore(term, title);
            foreach (var keyword in words)
            {
                if (best >= KeywordPrefix) break;
                best = Math.Max(best, KeywordScore(term, keyword));
            }

            if (best == 0) return 0;
            total += best;
        }

        return total;
    }

    /// <summary>
    /// The candidates matching <paramref name="query"/>, best first. An empty
    /// query lists the commands alone, in the order given.
    /// </summary>
    public static IReadOnlyList<PaletteCandidate<T>> Rank<T>(
        string query, IEnumerable<PaletteCandidate<T>> candidates, int perCategory, int total)
    {
        if (Terms(query).Length == 0)
        {
            return [.. candidates.Where(c => c.Category == PaletteCategory.Command).Take(total)];
        }

        return [.. candidates
            .Select((candidate, index) => (candidate, index, score: Score(query, candidate.Title, candidate.Keywords)))
            .Where(scored => scored.score > 0)
            .OrderByDescending(scored => scored.score)
            .ThenBy(scored => scored.candidate.Category)
            .ThenBy(scored => scored.candidate.Title.Length)
            .ThenBy(scored => scored.index)
            .GroupBy(scored => scored.candidate.Category)
            .SelectMany(group => group.Take(perCategory))
            .OrderByDescending(scored => scored.score)
            .ThenBy(scored => scored.candidate.Category)
            .ThenBy(scored => scored.candidate.Title.Length)
            .ThenBy(scored => scored.index)
            .Take(total)
            .Select(scored => scored.candidate)];
    }

    private static string[] Terms(string query) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int TermScore(string term, string text)
    {
        if (text.Length == 0) return 0;
        if (text.Equals(term, StringComparison.OrdinalIgnoreCase)) return Exact;
        if (text.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return Prefix;
        var at = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return 0;
        // A match at the start of a word ("scene_05" for "05", "Fanbox 外掛"
        // for "外掛") is a better guess than one in the middle of one.
        do
        {
            if (!char.IsLetterOrDigit(text[at - 1])) return WordStart;
            at = text.IndexOf(term, at + 1, StringComparison.OrdinalIgnoreCase);
        }
        while (at > 0);
        return Contains;
    }

    private static int KeywordScore(string term, string keyword)
    {
        if (keyword.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return KeywordPrefix;
        return keyword.Contains(term, StringComparison.OrdinalIgnoreCase) ? KeywordContains : 0;
    }
}

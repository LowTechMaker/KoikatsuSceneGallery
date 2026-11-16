using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>One scene card as the themed shelves see it: plain facts, no UI types.</summary>
/// <param name="AuthorKey">A stable author identity, or null when unknown.</param>
/// <param name="AuthorName">The author's display name.</param>
/// <param name="PostKey">The card's post (a grouping key), or null.</param>
/// <param name="Tags">The post's tags as shown (translation when there is one).</param>
/// <param name="Used">Ever dragged or copied out of the app.</param>
internal sealed record ThemeCard(
    string Path,
    string? AuthorKey,
    string? AuthorName,
    string? PostKey,
    IReadOnlyList<string> Tags,
    bool Used,
    DateTimeOffset? LastUsed,
    bool Liked,
    bool Hidden,
    DateTime Added);

internal enum ThemeKind { Author, Tag, NeverPlayed, DustyFavourites, SimilarToLast }

/// <param name="Subject">What the title names: the author's name, the tag, or the path of the card the
/// shelf is similar to; null for the kinds that name nothing.</param>
/// <param name="SubjectKey">What "see all" opens: the author key, or the tag; null otherwise.</param>
internal sealed record ThemeShelf(ThemeKind Kind, string? Subject, string? SubjectKey, IReadOnlyList<string> CardPaths);

/// <summary>
/// The home page's themed shelves for the day: a few shelves chosen from what
/// the library actually contains, different tomorrow, the same all day.
/// </summary>
/// <remarks>
/// Pure: the same cards, date and reroll counter give the same shelves in the
/// same order (seeded from them; no clock and no shared random inside). The
/// rules, each tested:
/// <list type="bullet">
/// <item>a theme is a candidate only with at least <see cref="MinCards"/> cards
/// after hidden ones are removed;</item>
/// <item>at most one shelf per kind, two for authors and for tags (different
/// subjects), and never only authors and tags when another kind is available;</item>
/// <item>authors are drawn weighted by the square root of their size, so a huge
/// author does not win every day, and boosted by their share of unplayed cards;</item>
/// <item>a tag says nothing when it covers more than a third of the library,
/// and is the author shelf again when all its cards are one author's;</item>
/// <item>inside a shelf: never-used cards first, then a seeded shuffle, at most
/// <see cref="MaxCards"/>, and no two neighbours of one post when avoidable;</item>
/// <item>two shelves sharing more than half their cards are not both chosen.</item>
/// </list>
/// </remarks>
internal static class HomeThemes
{
    public const int MinCards = 6;
    public const int MaxCards = 30;
    /// <summary>Cards not used for this many days count as dusty.</summary>
    public const int DustyDays = 30;
    /// <summary>The largest pHash distance (of 64 bits) that still looks alike.</summary>
    public const int SimilarDistance = 10;

    private const int AuthorDraws = 2;
    private const int TagDraws = 2;

    public static IReadOnlyList<ThemeShelf> Pick(
        IReadOnlyList<ThemeCard> cards, DateOnly today, int reroll, int count,
        Func<string, ulong?>? fingerprint = null)
    {
        if (count <= 0 || cards.Count == 0) return [];
        var random = new Random(Seed(cards, today, reroll));
        var visible = cards.Where(c => !c.Hidden).ToList();
        if (visible.Count < MinCards) return [];

        var candidates = new List<(ThemeKind Kind, string? Subject, string? Key, List<ThemeCard> Cards)>();

        // Authors: weighted draws without replacement.
        var authors = visible.Where(c => c.AuthorKey is not null)
            .GroupBy(c => c.AuthorKey!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= MinCards)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => (g.Key, Cards: g.ToList()))
            .ToList();
        foreach (var (key, authorCards) in Draw(authors, a =>
                     Math.Sqrt(a.Cards.Count) * (0.25 + (double)a.Cards.Count(c => !c.Used) / a.Cards.Count),
                     AuthorDraws, random))
            candidates.Add((ThemeKind.Author, authorCards[0].AuthorName ?? key, key, authorCards));

        // Tags that say something about a few cards of more than one author.
        var tags = visible
            .SelectMany(c => c.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Select(t => (Tag: t, Card: c)))
            .GroupBy(x => x.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Tag: g.Key, Cards: g.Select(x => x.Card).ToList()))
            .Where(t => t.Cards.Count >= MinCards && t.Cards.Count * 3 <= visible.Count && !OneAuthor(t.Cards))
            .OrderBy(t => t.Tag, StringComparer.Ordinal)
            .ToList();
        foreach (var (tag, tagCards) in Draw(tags, t => Math.Sqrt(t.Cards.Count), TagDraws, random))
            candidates.Add((ThemeKind.Tag, tag, tag, tagCards));

        var never = visible.Where(c => !c.Used).ToList();
        if (never.Count >= MinCards) candidates.Add((ThemeKind.NeverPlayed, null, null, never));

        var dustyBefore = new DateTimeOffset(today.ToDateTime(TimeOnly.MinValue)).AddDays(-DustyDays);
        var dusty = visible.Where(c => c.Liked && (c.LastUsed is null || c.LastUsed < dustyBefore)).ToList();
        if (dusty.Count >= MinCards) candidates.Add((ThemeKind.DustyFavourites, null, null, dusty));

        if (fingerprint is not null
            && cards.Where(c => c.LastUsed is not null).MaxBy(c => c.LastUsed) is { } last
            && fingerprint(last.Path) is { } lastPrint)
        {
            var similar = visible
                .Where(c => !string.Equals(c.Path, last.Path, StringComparison.OrdinalIgnoreCase))
                .Select(c => (Card: c, Print: fingerprint(c.Path)))
                .Where(x => x.Print is { } p && ImageFingerprintComparison.HammingDistance(p, lastPrint) <= SimilarDistance)
                .OrderBy(x => ImageFingerprintComparison.HammingDistance(x.Print!.Value, lastPrint))
                .Select(x => x.Card)
                .ToList();
            if (similar.Count >= MinCards) candidates.Add((ThemeKind.SimilarToLast, last.Path, null, similar));
        }

        // Each candidate's shelf as it would be shown; then a seeded order and
        // the greedy choice under the rules.
        var shelves = candidates
            .Select(c => new ThemeShelf(c.Kind, c.Subject, c.Key, Arrange(c.Cards, random)))
            .ToList();
        Shuffle(shelves, random);

        var chosen = new List<ThemeShelf>();
        while (chosen.Count < count)
        {
            var allowed = shelves.Where(s => Allowed(s, chosen)).ToList();
            if (allowed.Count == 0) break;
            // The last slot must not make the page only authors and tags when another kind fits.
            if (chosen.Count == count - 1 && chosen.Count > 0 && chosen.All(s => IsAuthorOrTag(s.Kind))
                && allowed.Any(s => !IsAuthorOrTag(s.Kind)))
                allowed = [.. allowed.Where(s => !IsAuthorOrTag(s.Kind))];
            var next = allowed[0];
            chosen.Add(next);
            shelves.Remove(next);
        }
        return chosen;
    }

    private static bool IsAuthorOrTag(ThemeKind kind) => kind is ThemeKind.Author or ThemeKind.Tag;

    private static bool Allowed(ThemeShelf shelf, List<ThemeShelf> chosen)
    {
        var sameKind = chosen.Count(s => s.Kind == shelf.Kind);
        if (sameKind >= (IsAuthorOrTag(shelf.Kind) ? 2 : 1)) return false;
        foreach (var other in chosen)
        {
            var shared = other.CardPaths.Intersect(shelf.CardPaths, StringComparer.OrdinalIgnoreCase).Count();
            if (shared * 2 > Math.Min(other.CardPaths.Count, shelf.CardPaths.Count)) return false;
        }
        return true;
    }

    private static bool OneAuthor(List<ThemeCard> cards)
        => cards.All(c => c.AuthorKey is not null)
           && cards.Select(c => c.AuthorKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;

    /// <summary>Never-used cards first, each group shuffled, at most <see cref="MaxCards"/>, no neighbours of one post when avoidable.</summary>
    private static List<string> Arrange(List<ThemeCard> cards, Random random)
    {
        var fresh = cards.Where(c => !c.Used).ToList();
        var used = cards.Where(c => c.Used).ToList();
        Shuffle(fresh, random);
        Shuffle(used, random);
        var arranged = new List<ThemeCard>(Math.Min(cards.Count, MaxCards));
        Spread(fresh, arranged);
        Spread(used, arranged);
        return [.. arranged.Take(MaxCards).Select(c => c.Path)];
    }

    /// <summary>
    /// Appends <paramref name="group"/> to <paramref name="arranged"/> so that no
    /// card follows one of the same post when that can be avoided: at each step,
    /// among the cards whose post differs from the previous card's, the one whose
    /// post has the most cards left (the classic way never to paint yourself into
    /// a corner), ties broken by the shuffled order. A group keeps its place:
    /// fresh cards stay before used ones.
    /// </summary>
    private static void Spread(List<ThemeCard> group, List<ThemeCard> arranged)
    {
        var left = new List<ThemeCard>(group);
        // Counted once and kept up to date: a group can hold thousands of cards
        // ("never played"), and only the first MaxCards are ever shown.
        var remaining = left
            .Where(c => c.PostKey is not null)
            .GroupBy(c => c.PostKey!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        while (left.Count > 0 && arranged.Count < MaxCards)
        {
            var previous = arranged.Count > 0 ? arranged[^1] : null;
            ThemeCard? pick = null;
            var best = -1;
            foreach (var card in left)
            {
                if (previous is not null && SamePost(previous, card)) continue;
                var weight = card.PostKey is null ? 0 : remaining[card.PostKey];
                if (weight > best)
                {
                    best = weight;
                    pick = card;
                }
            }
            // Unavoidable: every card left is of the previous card's post.
            pick ??= left[0];
            arranged.Add(pick);
            left.Remove(pick);
            if (pick.PostKey is not null) remaining[pick.PostKey]--;
        }
    }

    private static bool SamePost(ThemeCard a, ThemeCard b)
        => a.PostKey is not null && b.PostKey is not null
           && string.Equals(a.PostKey, b.PostKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Up to <paramref name="draws"/> items, each drawn with probability proportional to its weight, without replacement.</summary>
    private static List<T> Draw<T>(List<T> items, Func<T, double> weight, int draws, Random random)
    {
        var pool = items.Select(i => (Item: i, Weight: Math.Max(0, weight(i)))).ToList();
        var drawn = new List<T>();
        while (drawn.Count < draws && pool.Count > 0)
        {
            var total = pool.Sum(p => p.Weight);
            if (total <= 0) break;
            var target = random.NextDouble() * total;
            var index = 0;
            for (var acc = pool[0].Weight; acc < target && index < pool.Count - 1; acc += pool[++index].Weight) { }
            drawn.Add(pool[index].Item);
            pool.RemoveAt(index);
        }
        return drawn;
    }

    private static void Shuffle<T>(List<T> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// A seed from the date, the reroll counter and the library. Not
    /// <see cref="string.GetHashCode()"/>, which changes from run to run.
    /// </summary>
    private static int Seed(IReadOnlyList<ThemeCard> cards, DateOnly today, int reroll)
    {
        unchecked
        {
            var hash = 2166136261u;
            void Mix(uint value) { hash = (hash ^ value) * 16777619u; }
            Mix((uint)today.DayNumber);
            Mix((uint)reroll);
            Mix((uint)cards.Count);
            // Order-independent: the library is a set of paths.
            uint paths = 0;
            foreach (var card in cards)
            {
                var h = 2166136261u;
                foreach (var ch in card.Path.ToUpperInvariant()) h = (h ^ ch) * 16777619u;
                paths ^= h;
            }
            Mix(paths);
            return (int)hash;
        }
    }
}

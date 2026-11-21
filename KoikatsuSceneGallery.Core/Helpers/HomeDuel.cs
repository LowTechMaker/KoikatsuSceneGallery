namespace KoikatsuSceneGallery.Helpers;

/// <summary>One card as the duel sees it.</summary>
/// <param name="PostKey">The card's post (a <c>GalleryGrouping</c> key), or null when it has none.</param>
/// <param name="AuthorKey">The card's author, or null when unknown.</param>
/// <param name="Used">Whether the card was ever dragged or copied out of the app.</param>
internal sealed record DuelCard(string Path, string? PostKey, string? AuthorKey, bool Used);

internal sealed record DuelPair(DuelCard Left, DuelCard Right);

/// <summary>
/// "This or that" on the home page: the next two cards to choose between.
/// </summary>
/// <remarks>
/// Keeps the session's state — which cards were shown, how many were picked —
/// so the page can leave and come back mid-game. The rules, each tested:
/// <list type="bullet">
/// <item>fewer than two eligible cards: no pair;</item>
/// <item>two cards of one post are never paired: that is not a choice;</item>
/// <item>two cards of one author only when no other pairing exists;</item>
/// <item>cards never used before cards that were (being used is a preference,
/// not an exclusion: the caller excludes hidden and queued cards);</item>
/// <item>no card is shown twice in a session until every eligible card has
/// been; then a new round starts rather than the game running dry.</item>
/// </list>
/// The caller owns the <see cref="Random"/>, as in <see cref="HomeDeal"/>.
/// </remarks>
internal sealed class HomeDuel(Random random)
{
    /// <summary>How many first cards are tried before giving up on a round; bounds the work on a big library.</summary>
    private const int FirstCardAttempts = 64;

    private readonly HashSet<string> _shown = new(PathComparison.Comparer);

    /// <summary>Cards shown this session (or this round, after a pool ran out).</summary>
    public IReadOnlyCollection<string> Shown => _shown;

    /// <summary>Cards picked this session.</summary>
    public int Picked { get; private set; }

    public void Pick() => Picked++;

    /// <summary>Forgets what was shown and picked.</summary>
    public void Reset()
    {
        _shown.Clear();
        Picked = 0;
    }

    /// <summary>
    /// The next pair from <paramref name="candidates"/> leaving out
    /// <paramref name="excluded"/> (hidden, already queued), and marks both shown.
    /// </summary>
    public DuelPair? Next(IReadOnlyList<DuelCard> candidates, Func<string, bool> excluded)
    {
        var eligible = candidates
            .Where(card => !excluded(card.Path))
            .DistinctBy(card => card.Path, PathComparison.Comparer)
            .ToList();
        if (eligible.Count < 2) return null;

        var pair = TryPair([.. eligible.Where(card => !_shown.Contains(card.Path))]);
        if (pair is null)
        {
            // The unshown cards cannot make a pair: the pool is exhausted, a new round starts.
            _shown.Clear();
            pair = TryPair(eligible);
        }
        if (pair is null) return null;

        _shown.Add(pair.Left.Path);
        _shown.Add(pair.Right.Path);
        return pair;
    }

    private DuelPair? TryPair(List<DuelCard> pool)
    {
        if (pool.Count < 2) return null;
        Shuffle(pool);
        // Stable after the shuffle: random among the fresh, then random among the used.
        var ordered = pool.OrderBy(card => card.Used).ToList();

        foreach (var first in ordered.Take(FirstCardAttempts))
        {
            DuelCard? best = null;
            var bestRank = int.MaxValue;
            foreach (var second in ordered)
            {
                if (ReferenceEquals(second, first) || SamePost(first, second)) continue;
                // Fresh cards come first in `ordered`, so the first best found is the freshest.
                var rank = Same(first.AuthorKey, second.AuthorKey) ? 1 : 0;
                if (rank < bestRank)
                {
                    best = second;
                    bestRank = rank;
                    if (rank == 0) break;
                }
            }
            if (best is not null)
                return random.Next(2) == 0 ? new DuelPair(first, best) : new DuelPair(best, first);
        }
        return null;
    }

    private static bool SamePost(DuelCard a, DuelCard b) => Same(a.PostKey, b.PostKey);

    // An unknown post or author is nobody's: it matches nothing, not even another unknown.
    private static bool Same(string? a, string? b)
        => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Shuffle(List<DuelCard> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }
}

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Deals the home page's "deal me a few" hand the way the discovery page deals
/// a poker hand: a non-repeating round (<see cref="DiscoveryShuffle{T}"/>),
/// <see cref="PokerHand.HandSize"/> cards, look-alikes spread apart
/// (<see cref="DiscoveryAntiClustering"/>), and now and then a joker from the
/// heaviest files.
/// </summary>
/// <remarks>
/// Keeps its shuffle between deals, so hand after hand works through the
/// library without repeats until every card has been dealt once, as the
/// discovery page's rounds do. The caller passes the candidates each time
/// (hidden cards already left out) and the shuffle is synchronized to them, so
/// a card hidden or deleted since the last deal is never dealt.
///
/// Generic over the card so the rules are testable without WinUI types.
/// </remarks>
internal sealed class HomeDeal(Random random)
{
    private readonly DiscoveryShuffle<string> _shuffle = new(random, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A hand of up to <see cref="PokerHand.HandSize"/> cards from
    /// <paramref name="candidates"/>, avoiding <paramref name="previous"/>
    /// when there are enough others to choose from.
    /// </summary>
    /// <param name="path">The card's file path, its identity here.</param>
    /// <param name="size">The card's file size, for spreading and the joker. Must not touch the disk.</param>
    public IReadOnlyList<T> Deal<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyCollection<string> previous,
        Func<T, string> path,
        Func<T, long> size)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(previous);
        var byPath = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in candidates) byPath.TryAdd(path(card), card);
        _shuffle.Synchronize(byPath.Keys);

        var avoid = new HashSet<string>(previous, StringComparer.OrdinalIgnoreCase);
        var hand = new List<string>(PokerHand.HandSize);
        var skipped = new List<string>();
        // Within a round the shuffle never repeats, so the last hand is avoided
        // for free. Across a round boundary the new round could open with the
        // cards just shown: those are drawn and set aside, which counts them as
        // seen in the new round, and only used if nothing else is left.
        var startedRound = false;
        while (hand.Count < PokerHand.HandSize)
        {
            if (_shuffle.Remaining == 0)
            {
                if (startedRound || byPath.Count == 0) break;
                _shuffle.StartRound(byPath.Keys);
                startedRound = true;
            }
            var drawn = _shuffle.Take(1)[0];
            if (avoid.Contains(drawn) || hand.Contains(drawn, StringComparer.OrdinalIgnoreCase))
            {
                skipped.Add(drawn);
                continue;
            }
            hand.Add(drawn);
        }
        // A library smaller than two hands: some repeats are unavoidable.
        foreach (var again in skipped)
        {
            if (hand.Count >= PokerHand.HandSize) break;
            if (!hand.Contains(again, StringComparer.OrdinalIgnoreCase)) hand.Add(again);
        }

        var cards = hand.Select(p => byPath[p]).ToList();
        // A shelf is one row, so every card in it is a neighbour of the next:
        // as many columns as cards, nothing placed before it.
        DiscoveryAntiClustering.Spread(
            cards, card => new(size(card), Path.GetDirectoryName(path(card)) ?? ""), Math.Max(1, cards.Count), []);
        // After the spread, never before, as on the discovery page: the joker
        // is an outlier in file size by definition and the spread would move it.
        DealJoker(cards, byPath, avoid, path, size);
        return cards;
    }

    private void DealJoker<T>(
        List<T> cards, Dictionary<string, T> byPath, HashSet<string> avoid, Func<T, string> path, Func<T, long> size)
        where T : class
    {
        if (cards.Count == 0 || !PokerHand.ShouldDealJoker(random)) return;
        var drawn = cards.Select(path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var joker = PokerHand
            .JokerCandidates(byPath.Values.ToArray(), size)
            .FirstOrDefault(c => !drawn.Contains(path(c)) && !avoid.Contains(path(c)));
        if (joker is null) return;
        cards[PokerHand.JokerSlot(random, cards.Count)] = joker;
    }
}

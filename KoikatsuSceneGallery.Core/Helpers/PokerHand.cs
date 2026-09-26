namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Turns the endless discovery round into a dealt hand of cards, with an
/// occasional "joker" drawn from the heaviest files in the library.
/// </summary>
/// <remarks>
/// Pure and parameterless by design: every knob lives here as a constant so a
/// later tuning pass touches one file and the tests keep their meaning. The
/// caller owns the <see cref="Random"/>, which is what makes the odds testable
/// with a fixed seed.
/// </remarks>
internal static class PokerHand
{
    /// <summary>Cards dealt per hand.</summary>
    public const int HandSize = 13;

    /// <summary>Chance that a given hand contains a joker.</summary>
    public const double JokerChance = 0.2;

    /// <summary>The joker is drawn from this top slice of the library by file size.</summary>
    public const double JokerTopFraction = 0.10;

    public static bool ShouldDealJoker(Random rng) => rng.NextDouble() < JokerChance;

    /// <summary>
    /// The heaviest <see cref="JokerTopFraction"/> of the candidates, largest
    /// first. Always yields at least one item for a non-empty library, so a
    /// small collection still gets a joker rather than silently never dealing
    /// one.
    /// </summary>
    public static IReadOnlyList<T> JokerCandidates<T>(
        IReadOnlyList<T> all, Func<T, long> size, double topFraction = JokerTopFraction)
    {
        if (all.Count == 0) return [];
        var take = Math.Max(1, (int)(all.Count * topFraction));
        return all.OrderByDescending(size).Take(take).ToArray();
    }

    /// <summary>Which position in the hand the joker replaces.</summary>
    public static int JokerSlot(Random rng, int handSize) =>
        handSize <= 0 ? -1 : rng.Next(handSize);
}

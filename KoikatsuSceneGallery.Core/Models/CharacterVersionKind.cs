namespace KoikatsuSceneGallery.Models;

/// <summary>
/// What a character card is, relative to the character's other cards.
/// </summary>
/// <remarks>
/// Deliberately only two values. Whether a card has been superseded is a
/// separate fact, tracked alongside this — a single three-way choice could not
/// say "a what-if version that a newer what-if has replaced", which is exactly
/// what happens when someone updates their variant.
/// </remarks>
public enum CharacterVersionKind
{
    /// <summary>The character itself. The default.</summary>
    Current = 0,

    /// <summary>
    /// A what-if variant: a sibling of the character rather than a newer or
    /// older take on it. Never displaces the card that represents the
    /// character, and stays visible in its own right.
    /// </summary>
    Alternate = 1,
}

/// <summary>
/// Converts kinds to and from the token stored in the sidecar.
/// </summary>
/// <remarks>
/// Parsing is deliberately tolerant instead of using
/// <c>JsonStringEnumConverter</c>: that throws on a value it does not know,
/// which would fail the whole document and silently drop every annotation in
/// the directory. An unknown token here costs one card its annotation.
/// </remarks>
public static class CharacterVersionKinds
{
    public const string CurrentToken = "current";
    public const string AlternateToken = "alternate";

    /// <summary>
    /// The token written before superseding became its own field. It meant
    /// "the character itself, replaced by something newer".
    /// </summary>
    public const string LegacySupersededToken = "old";

    public static string ToToken(this CharacterVersionKind kind)
        => kind == CharacterVersionKind.Alternate ? AlternateToken : CurrentToken;

    public static CharacterVersionKind Parse(string? token)
        => string.Equals(token, AlternateToken, StringComparison.OrdinalIgnoreCase)
            ? CharacterVersionKind.Alternate
            : CharacterVersionKind.Current;

    /// <summary>Whether a token is the old marker that also implied superseded.</summary>
    public static bool IsLegacySuperseded(string? token)
        => string.Equals(token, LegacySupersededToken, StringComparison.OrdinalIgnoreCase);
}

using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Which platform's cards the galleries are showing: one of them, or all.
/// </summary>
/// <remarks>
/// This replaced a three-way online/local/all switch. The split it offered did
/// not match the library it was filtering — of 21,994 scenes, 66 were local, so
/// "online" and "all" showed the same page and the third option was the only
/// one that did anything. The platforms themselves are the division that
/// actually partitions the library (pixiv 18,833, BepisDB 1,495, Fanbox 1,326),
/// and local becomes one platform among them rather than half of the control.
///
/// A platform is named by its provider id, so local needs no special case here:
/// it is the built-in provider and selecting it behaves like selecting any
/// other.
/// </remarks>
public readonly record struct CardOriginSelection
{
    /// <summary>Values the old three-way switch stored, still readable from config.</summary>
    private const string LegacyAll = "All";
    private const string LegacyExcludeLocal = "ExcludeLocal";
    private const string LegacyLocalOnly = "LocalOnly";

    /// <summary>The chosen platform, or null for all of them.</summary>
    public string? ProviderId { get; private init; }

    /// <summary>Every card, whatever platform it came from.</summary>
    public static CardOriginSelection All => default;

    public bool IsAll => string.IsNullOrEmpty(ProviderId);

    public bool IsLocal => LocalSourceIdentity.IsLocal(ProviderId);

    /// <summary>The selection for one platform; blank means all.</summary>
    public static CardOriginSelection For(string? providerId)
        => string.IsNullOrWhiteSpace(providerId) ? All : new() { ProviderId = providerId.Trim() };

    /// <summary>Only local cards.</summary>
    public static CardOriginSelection Local => For(LocalSourceIdentity.ProviderId);

    /// <summary>
    /// What goes in config.json. A provider id, or empty for all.
    /// </summary>
    public string ToConfigValue() => ProviderId ?? string.Empty;

    /// <summary>
    /// Reads a stored value, including one written by the old three-way switch.
    /// </summary>
    /// <remarks>
    /// "LocalOnly" becomes the local platform, which is the same set of cards
    /// under the new control. "ExcludeLocal" has no equivalent — no single
    /// platform means "all but one" — so it becomes All. That widens what the
    /// user sees rather than narrowing it, which is the safe direction for a
    /// setting they cannot see being migrated.
    ///
    /// A provider whose id happens to be one of the legacy words would be
    /// misread. The three ids in play are pixiv, bepisdb and fanbox.
    /// </remarks>
    public static CardOriginSelection Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return All;
        var value = stored.Trim();

        if (string.Equals(value, LegacyAll, StringComparison.OrdinalIgnoreCase)) return All;
        if (string.Equals(value, LegacyExcludeLocal, StringComparison.OrdinalIgnoreCase)) return All;
        if (string.Equals(value, LegacyLocalOnly, StringComparison.OrdinalIgnoreCase)) return Local;

        return For(value);
    }
}

/// <summary>
/// Decides whether a card passes the platform selection. A card's platform is
/// the provider id of the author resolved from its folder.
/// </summary>
public static class CardOriginQuery
{
    /// <summary>
    /// Whether a card from <paramref name="providerId"/> is shown.
    /// </summary>
    /// <remarks>
    /// A card whose author never resolved has no platform, so it appears only
    /// under All. The old switch let such a card through as "not local"; under
    /// a platform switch there is no platform it could honestly belong to, and
    /// showing it under an arbitrary one would be worse than leaving it to All.
    /// </remarks>
    public static bool Passes(string? providerId, CardOriginSelection selection)
        => selection.IsAll
           || (!string.IsNullOrEmpty(providerId)
               && string.Equals(providerId, selection.ProviderId, StringComparison.OrdinalIgnoreCase));
}

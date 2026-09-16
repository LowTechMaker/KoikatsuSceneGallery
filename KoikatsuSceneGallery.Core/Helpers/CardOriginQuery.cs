using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Gallery filter over where a card came from, as opposed to what it contains.
/// </summary>
public enum CardOriginFilter
{
    /// <summary>Every card, whatever its source.</summary>
    All,

    /// <summary>Hide locally collected cards.</summary>
    ExcludeLocal,

    /// <summary>Show only locally collected cards.</summary>
    LocalOnly,
}

/// <summary>
/// Decides whether a card passes the origin filter. A card's origin is the
/// provider id of the author resolved from its folder; cards with no author at
/// all count as not-local, so they stay visible while local cards are hidden.
/// </summary>
public static class CardOriginQuery
{
    public static bool Passes(string? providerId, CardOriginFilter filter)
        => filter switch
        {
            CardOriginFilter.ExcludeLocal => !LocalSourceIdentity.IsLocal(providerId),
            CardOriginFilter.LocalOnly => LocalSourceIdentity.IsLocal(providerId),
            _ => true,
        };
}

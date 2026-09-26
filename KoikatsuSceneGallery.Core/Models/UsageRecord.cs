namespace KoikatsuSceneGallery.Models;

/// <summary>
/// One recorded use of a card: the moment it left the app for somewhere else.
/// </summary>
/// <param name="At">When it happened.</param>
/// <param name="Operation">The call site that recorded it, e.g. "Browser.Drag".</param>
/// <param name="FilePath">The card's file at the time of use.</param>
/// <param name="OriginUrl">The page the card came from, or null when none is derivable.</param>
public sealed record UsageRecord(DateTimeOffset At, string Operation, string FilePath, string? OriginUrl);

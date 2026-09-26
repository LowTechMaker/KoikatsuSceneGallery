using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

/// <summary>Unique identity for each occurrence, including repeated cards in later rounds.</summary>
/// <param name="IsJoker">
/// Set only in poker-hand mode, for the one card swapped in from the heaviest
/// slice of the library.
/// </param>
public sealed record DiscoveryItem(SceneCard Card, int Round, bool IsJoker = false)
{
    public string JokerText => UiText.Get("Discovery_Joker");
}

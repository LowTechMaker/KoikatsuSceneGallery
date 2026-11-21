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

    /// <summary>
    /// The caption under the picture follows the app-wide "show file names"
    /// switch, as the library's does (read when the tile is bound).
    /// </summary>
    public Microsoft.UI.Xaml.Visibility CaptionVisibility =>
        App.Services.GetRequiredService<ViewModels.SettingsViewModel>().ShowFileNames
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;
}

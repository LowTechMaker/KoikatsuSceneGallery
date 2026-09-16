using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Helpers;

internal sealed record SceneDiscoveryFilter(
    string[] Keywords, bool ShowR18, GameFilterOption Game, bool FilterResolution, HashSet<string> Resolutions)
{
    public bool Matches(SceneCard card)
        => Matches(card, Keywords, ShowR18, Game, FilterResolution, Resolutions);

    public static bool Matches(SceneCard card, IReadOnlyList<string> keywords, bool showR18,
        GameFilterOption game, bool filterResolution, HashSet<string> resolutions)
    {
        if (!showR18 && card.IsR18Content) return false;
        if (!GallerySearch.Matches(card.FilePath, card.Author?.Name, keywords)) return false;
        if (filterResolution && resolutions.Count > 0 && !resolutions.Contains(card.Resolution)) return false;
        if (game == GameFilterOption.All) return true;
        var target = game switch
        {
            GameFilterOption.Koikatsu => GameVersion.Koikatsu,
            GameFilterOption.KoikatsuSunshine => GameVersion.KoikatsuSunshine,
            _ => GameVersion.Unknown
        };
        return card.MetadataLoaded && card.Game == target;
    }
}

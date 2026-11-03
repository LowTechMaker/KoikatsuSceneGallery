using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery.Helpers;

internal sealed record SceneDiscoveryFilter(
    string[] Keywords, RatingFilterOption Rating, GameFilterOption Game, bool FilterResolution, HashSet<string> Resolutions,
    CardOriginSelection Origin = default)
{
    public bool Matches(SceneCard card)
        => Matches(card, Keywords, Rating, Game, FilterResolution, Resolutions, Origin);

    public static bool Matches(SceneCard card, IReadOnlyList<string> keywords, RatingFilterOption rating,
        GameFilterOption game, bool filterResolution, HashSet<string> resolutions,
        CardOriginSelection origin = default)
    {
        // Discovery draws from the same library as the grids, so it has to obey
        // the global online/local switch or the two views disagree.
        if (!CardOriginQuery.Passes(CardOrigin.ProviderIdOf(card), origin)) return false;
        if (!RatingPasses(rating, card.Rating)) return false;
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

    public static bool RatingPasses(RatingFilterOption filter, int rating) => filter switch
    {
        RatingFilterOption.GeneralOnly => rating == SceneRating.General,
        RatingFilterOption.R18AndR18G => rating >= SceneRating.R18,
        RatingFilterOption.R18Only => rating == SceneRating.R18,
        RatingFilterOption.R18GOnly => rating == SceneRating.R18G,
        _ => true,
    };
}

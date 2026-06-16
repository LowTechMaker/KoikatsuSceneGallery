using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Pages;

public sealed class AuthorDetailNavigationParameter
{
    public AuthorDetailNavigationParameter(AuthorSummary summary)
    {
        Summary = summary;
    }

    public AuthorSummary Summary { get; }

    public int? RestoreSelectedTabOnBack { get; set; }
}

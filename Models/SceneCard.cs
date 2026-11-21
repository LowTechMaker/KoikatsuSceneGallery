using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

public partial class SceneCard : CardBase, IAuthorOwner
{
    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    [ObservableProperty]
    public partial GameVersion Game { get; set; } = GameVersion.Unknown;

    /// <summary>
    /// The rating the gallery filters and labels by (0 = G, 1 = R-18, 2 = R-18G),
    /// resolved from the source chosen in settings; see <see cref="SceneRating"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsR18Content))]
    public partial int Rating { get; set; }

    public bool IsR18Content => Rating >= SceneRating.R18;

    /// <summary>The rating implied by the rating folder the card sits under; the fallback when no post data exists.</summary>
    public int FolderRating { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;
}

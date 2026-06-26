using CommunityToolkit.Mvvm.ComponentModel;

namespace KoikatsuSceneGallery.Models;

public partial class CoordinateCard : CardBase, IAuthorOwner
{
    public DateTime DateCreated { get; init; }

    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    [ObservableProperty]
    public partial string CoordinateName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;
}

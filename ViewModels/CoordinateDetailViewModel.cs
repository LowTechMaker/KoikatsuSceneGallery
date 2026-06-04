using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class CoordinateDetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial CoordinateCard? Card { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsParsing))]
    public partial bool MetadataLoaded { get; set; }

    public bool IsParsing => !MetadataLoaded;

    [ObservableProperty]
    public partial string CoordinateName { get; set; } = string.Empty;
}

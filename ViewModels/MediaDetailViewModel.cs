using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class MediaDetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial MediaCard? Card { get; set; }
}

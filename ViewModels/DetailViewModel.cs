using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

public partial class DetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial SceneCard? Card { get; set; }
}

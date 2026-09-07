using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class GalleryPage : Page
{
    public GalleryViewModel ViewModel { get; } = App.Services.GetRequiredService<GalleryViewModel>();
    public GalleryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Browser.Initialize(LibraryKind.Scenes);
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); Browser.Activate(Frame); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Browser.Deactivate(); base.OnNavigatedFrom(e); }
}

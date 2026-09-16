using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CoordinateGalleryPage : Page
{
    public CoordinateGalleryViewModel ViewModel { get; } = App.Services.GetRequiredService<CoordinateGalleryViewModel>();
    public CoordinateGalleryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Browser.Initialize(LibraryKind.Coordinates);
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); Browser.Activate(Frame); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Browser.Deactivate(); base.OnNavigatedFrom(e); }
}

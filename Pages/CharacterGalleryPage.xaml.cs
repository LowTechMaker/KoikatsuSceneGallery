using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CharacterGalleryPage : Page
{
    public CharacterGalleryViewModel ViewModel { get; } = App.Services.GetRequiredService<CharacterGalleryViewModel>();
    public CharacterGalleryPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        Browser.Initialize(LibraryKind.Characters);
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); Browser.Activate(Frame); }
    protected override void OnNavigatedFrom(NavigationEventArgs e) { Browser.Deactivate(); base.OnNavigatedFrom(e); }
}

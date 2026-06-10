using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorsPage : Page
{
    public AuthorsViewModel ViewModel { get; }

    public AuthorsPage()
    {
        ViewModel = App.AuthorsViewModel;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Counts come from the gallery collections, so galleries the user
        // hasn't visited yet would show as zero — load any that are empty.
        if (App.GalleryViewModel.Cards.Count == 0 && !App.GalleryViewModel.IsLoading)
            _ = App.GalleryViewModel.LoadCardsCommand.ExecuteAsync(null);
        if (App.CharacterGalleryViewModel.Cards.Count == 0 && !App.CharacterGalleryViewModel.IsLoading)
            _ = App.CharacterGalleryViewModel.LoadCardsCommand.ExecuteAsync(null);
        if (App.CoordinateGalleryViewModel.Cards.Count == 0 && !App.CoordinateGalleryViewModel.IsLoading)
            _ = App.CoordinateGalleryViewModel.LoadCardsCommand.ExecuteAsync(null);
    }

    private async void AuthorsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorSummary summary)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
    }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
    }

    private async void RefreshOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await ViewModel.RefreshOneAsync(summary);
    }
}

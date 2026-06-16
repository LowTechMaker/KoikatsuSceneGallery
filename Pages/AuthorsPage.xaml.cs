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
        _ = App.EnsureAuthorSourcesLoadedAsync();
    }

    private void AuthorsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorSummary summary)
            OpenAuthorDetail(summary);
    }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await Windows.System.Launcher.LaunchUriAsync(new Uri(summary.Display.ProfileUrl));
    }

    private void OpenAuthorDetail(AuthorSummary summary)
        => Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));

    private async void RefreshOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorSummary summary })
            await ViewModel.RefreshOneAsync(summary);
    }
}

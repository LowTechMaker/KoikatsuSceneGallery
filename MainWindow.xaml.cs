using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KoikatsuSceneGallery.Pages;

namespace KoikatsuSceneGallery;

public sealed partial class MainWindow : Window
{
    private static readonly InfoBadge ImportingBadge = new()
    {
        Style = (Style)Application.Current.Resources["AttentionIconInfoBadgeStyle"],
    };

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        if (App.AuthorInfoService.IsAvailable)
            AuthorsNavItem.Visibility = Visibility.Visible;

        if (App.ImportViewModel is not null)
        {
            ImportNavItem.Visibility = Visibility.Visible;
            App.ImportViewModel.PropertyChanged += ImportViewModel_PropertyChanged;
        }

        NavFrame.Navigate(typeof(GalleryPage));
    }

    private void ImportViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.ImportViewModel.IsImporting))
            ImportNavItem.InfoBadge = App.ImportViewModel!.IsImporting ? ImportingBadge : null;
    }

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (NavFrame.CanGoBack)
            NavFrame.GoBack();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "gallery":
                    NavFrame.Navigate(typeof(GalleryPage));
                    break;
                case "characters":
                    NavFrame.Navigate(typeof(CharacterGalleryPage));
                    break;
                case "coordinates":
                    NavFrame.Navigate(typeof(CoordinateGalleryPage));
                    break;
                case "screenshots":
                    NavFrame.Navigate(typeof(ScreenshotGalleryPage));
                    break;
                case "videos":
                    NavFrame.Navigate(typeof(VideoGalleryPage));
                    break;
                case "authors" when App.AuthorInfoService.IsAvailable:
                    NavFrame.Navigate(typeof(AuthorsPage));
                    break;
                case "import" when App.ImportViewModel is not null:
                    NavFrame.Navigate(typeof(ImportPage));
                    break;
            }
        }
    }
}

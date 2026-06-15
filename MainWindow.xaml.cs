using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using KoikatsuSceneGallery.Pages;

namespace KoikatsuSceneGallery;

public sealed partial class MainWindow : Window
{
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
            UpdateImportNavBadge();
        }

        NavFrame.Navigate(typeof(GalleryPage));
    }

    private void ImportViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.ImportViewModel.IsImporting)
            or nameof(ViewModels.ImportViewModel.IsAnalyzing)
            or nameof(ViewModels.ImportViewModel.AnalysisPendingCount)
            or nameof(ViewModels.ImportViewModel.AnalysisStatusText))
        {
            UpdateImportNavBadge();
        }
    }

    private void UpdateImportNavBadge()
    {
        if (App.ImportViewModel is not { } viewModel)
            return;

        if (viewModel.IsImporting)
        {
            ImportNavItem.InfoBadge = CreateImportStatusBadge();
            ToolTipService.SetToolTip(ImportNavItem, "Importing");
            return;
        }

        if (viewModel.IsAnalyzing)
        {
            var pendingCount = viewModel.AnalysisPendingCount;
            ImportNavItem.InfoBadge = pendingCount > 0
                ? CreateImportStatusBadge(pendingCount)
                : CreateImportStatusBadge();
            ToolTipService.SetToolTip(ImportNavItem, $"Analyzing {pendingCount} pending");
            return;
        }

        ImportNavItem.InfoBadge = null;
        ToolTipService.SetToolTip(ImportNavItem, null);
    }

    private static InfoBadge CreateImportStatusBadge(int? value = null)
    {
        var badge = new InfoBadge
        {
            Style = (Style)Application.Current.Resources["AttentionIconInfoBadgeStyle"],
        };

        if (value is not null)
            badge.Value = value.Value;

        return badge;
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

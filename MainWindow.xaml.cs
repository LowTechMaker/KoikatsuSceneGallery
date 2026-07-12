using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery;

public sealed partial class MainWindow : Window
{
    private bool _suppressLibrarySelectionChanged;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        if (App.Services.GetRequiredService<AuthorInfoService>().IsAvailable)
            AuthorsNavItem.Visibility = Visibility.Visible;

        if (App.Services.GetService<ImportViewModel>() is { } importViewModel)
        {
            ImportNavItem.Visibility = Visibility.Visible;
            importViewModel.PropertyChanged += ImportViewModel_PropertyChanged;
            UpdateImportNavBadge();
        }

        ApplyNavVisibility();
        App.Services.GetRequiredService<SettingsViewModel>().NavItemVisibilityChanged += OnNavItemVisibilityChanged;

        NavigateToSelectedLibraryPage();
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
        if (App.Services.GetService<ImportViewModel>() is not { } viewModel)
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

    private void ApplyNavVisibility()
    {
        var vm = App.Services.GetRequiredService<SettingsViewModel>();
        SetNavItemVisibility("gallery", vm.ShowGalleryNav);
        SetNavItemVisibility("characters", vm.ShowCharactersNav);
        SetNavItemVisibility("coordinates", vm.ShowCoordinatesNav);
        SetNavItemVisibility("screenshots", vm.ShowScreenshotsNav);
        SetNavItemVisibility("videos", vm.ShowVideosNav);
    }

    private void OnNavItemVisibilityChanged(string tag, bool visible)
    {
        DispatcherQueue.TryEnqueue(() => SetNavItemVisibility(tag, visible));
    }

    private void SetNavItemVisibility(string tag, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        switch (tag)
        {
            case "gallery":
                ScenesSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "characters":
                CharactersSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "coordinates":
                CoordinatesSelectorItem.Visibility = visibility;
                UpdateLibraryNavVisibility();
                break;
            case "screenshots":
                ScreenshotsNavItem.Visibility = visibility;
                break;
            case "videos":
                VideosNavItem.Visibility = visibility;
                break;
        }
    }

    private void UpdateLibraryNavVisibility()
    {
        var visibleItems = new[]
        {
            ScenesSelectorItem,
            CharactersSelectorItem,
            CoordinatesSelectorItem,
        }.Where(item => item.Visibility == Visibility.Visible).ToList();

        LibraryNavItem.Visibility = visibleItems.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (LibrarySelectorBar.SelectedItem is SelectorBarItem selected
            && selected.Visibility == Visibility.Visible)
        {
            return;
        }

        var next = visibleItems.FirstOrDefault() ?? ScenesSelectorItem;
        _suppressLibrarySelectionChanged = true;
        LibrarySelectorBar.SelectedItem = next;
        _suppressLibrarySelectionChanged = false;

        if (NavFrame is not null
            && ReferenceEquals(NavView.SelectedItem, LibraryNavItem)
            && IsLibraryPage(NavFrame.CurrentSourcePageType))
        {
            NavigateToSelectedLibraryPage(replaceCurrentLibraryPage: true);
        }
    }

    private void LibrarySelectorBar_SelectionChanged(
        SelectorBar sender,
        SelectorBarSelectionChangedEventArgs args)
    {
        if (_suppressLibrarySelectionChanged
            || NavFrame is null
            || !ReferenceEquals(NavView.SelectedItem, LibraryNavItem))
        {
            return;
        }

        NavigateToSelectedLibraryPage(replaceCurrentLibraryPage: true);
    }

    private void NavigateToSelectedLibraryPage(bool replaceCurrentLibraryPage = false)
    {
        var pageType = LibrarySelectorBar.SelectedItem switch
        {
            var item when item == CharactersSelectorItem => typeof(CharacterGalleryPage),
            var item when item == CoordinatesSelectorItem => typeof(CoordinateGalleryPage),
            _ => typeof(GalleryPage),
        };

        var previousPageType = NavFrame.CurrentSourcePageType;
        if (previousPageType == pageType)
            return;

        if (!NavFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo()))
            return;

        if (replaceCurrentLibraryPage
            && IsLibraryPage(previousPageType)
            && NavFrame.BackStack.Count > 0)
        {
            NavFrame.BackStack.RemoveAt(NavFrame.BackStack.Count - 1);
        }
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var isLibraryPage = IsLibraryPage(e.SourcePageType);
        LibrarySelectorBar.Visibility = isLibraryPage
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!isLibraryPage)
            return;

        var selectedItem = e.SourcePageType switch
        {
            var type when type == typeof(CharacterGalleryPage) => CharactersSelectorItem,
            var type when type == typeof(CoordinateGalleryPage) => CoordinatesSelectorItem,
            _ => ScenesSelectorItem,
        };

        if (LibrarySelectorBar.SelectedItem != selectedItem)
        {
            _suppressLibrarySelectionChanged = true;
            LibrarySelectorBar.SelectedItem = selectedItem;
            _suppressLibrarySelectionChanged = false;
        }

        NavView.SelectedItem = LibraryNavItem;
    }

    private static bool IsLibraryPage(Type? pageType) =>
        pageType == typeof(GalleryPage)
        || pageType == typeof(CharacterGalleryPage)
        || pageType == typeof(CoordinateGalleryPage);

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
                case "library":
                    NavigateToSelectedLibraryPage();
                    break;
                case "screenshots":
                    NavFrame.Navigate(typeof(ScreenshotGalleryPage));
                    break;
                case "videos":
                    NavFrame.Navigate(typeof(VideoGalleryPage));
                    break;
                case "authors" when App.Services.GetRequiredService<AuthorInfoService>().IsAvailable:
                    NavFrame.Navigate(typeof(AuthorsPage));
                    break;
                case "import" when App.Services.GetService<ImportViewModel>() is not null:
                    NavFrame.Navigate(typeof(ImportPage));
                    break;
            }
        }
    }
}

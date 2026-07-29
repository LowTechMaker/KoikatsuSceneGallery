using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;

namespace KoikatsuSceneGallery;

public sealed partial class MainWindow : Window
{
    private static readonly TimeSpan NavigationDebounceInterval = TimeSpan.FromMilliseconds(100);

    private bool _suppressLibrarySelectionChanged;
    private DispatcherQueueTimer? _navigationDebounceTimer;
    private Type? _pendingPageType;
    private bool _pendingReplaceCurrentLibraryPage;
    private bool _isBackNavigationInProgress;
    private bool _releaseBackNavigationOnNextFrame;

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
        => TryGoBack(NavFrame);

    internal bool TryGoBack(Frame frame)
    {
        if (!ReferenceEquals(frame, NavFrame)
            || _isBackNavigationInProgress
            || !frame.CanGoBack)
        {
            return false;
        }

        _isBackNavigationInProgress = true;
        AppTitleBar.IsBackButtonEnabled = false;

        try
        {
            frame.GoBack();
            return true;
        }
        catch (Exception ex)
        {
            CompleteBackNavigation();
            App.Services.GetRequiredService<IAppLogger>()
                .LogError("MainWindow.GoBack", ex);
            return false;
        }
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

        QueueNavigationToSelectedLibraryPage(replaceCurrentLibraryPage: true);
    }

    private void NavigateToSelectedLibraryPage(bool replaceCurrentLibraryPage = false)
    {
        var pageType = LibrarySelectorBar.SelectedItem switch
        {
            var item when item == CharactersSelectorItem => typeof(CharacterGalleryPage),
            var item when item == CoordinatesSelectorItem => typeof(CoordinateGalleryPage),
            _ => typeof(GalleryPage),
        };

        NavigateToPage(pageType, replaceCurrentLibraryPage);
    }

    private void QueueNavigationToSelectedLibraryPage(bool replaceCurrentLibraryPage = false)
    {
        var pageType = LibrarySelectorBar.SelectedItem switch
        {
            var item when item == CharactersSelectorItem => typeof(CharacterGalleryPage),
            var item when item == CoordinatesSelectorItem => typeof(CoordinateGalleryPage),
            _ => typeof(GalleryPage),
        };

        QueueNavigation(pageType, replaceCurrentLibraryPage);
    }

    private void QueueNavigation(Type pageType, bool replaceCurrentLibraryPage = false)
    {
        _pendingPageType = pageType;
        _pendingReplaceCurrentLibraryPage = replaceCurrentLibraryPage;

        if (_navigationDebounceTimer is null)
        {
            _navigationDebounceTimer = DispatcherQueue.CreateTimer();
            _navigationDebounceTimer.Interval = NavigationDebounceInterval;
            _navigationDebounceTimer.IsRepeating = false;
            _navigationDebounceTimer.Tick += NavigationDebounceTimer_Tick;
        }

        _navigationDebounceTimer.Stop();
        _navigationDebounceTimer.Start();
    }

    private void NavigationDebounceTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        var pageType = _pendingPageType;
        var replaceCurrentLibraryPage = _pendingReplaceCurrentLibraryPage;
        _pendingPageType = null;
        _pendingReplaceCurrentLibraryPage = false;

        if (pageType is not null)
            NavigateToPage(pageType, replaceCurrentLibraryPage);
    }

    private void NavigateToPage(Type pageType, bool replaceCurrentLibraryPage = false)
    {
        var previousPageType = NavFrame.CurrentSourcePageType;
        if (previousPageType == pageType)
            return;

#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[Navigation] {previousPageType?.Name ?? "<none>"} -> {pageType.Name}");
#endif

        try
        {
            if (!NavFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo()))
                return;
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<IAppLogger>()
                .LogError("MainWindow.Navigate", ex, pageType.FullName);
            return;
        }

        if (replaceCurrentLibraryPage
            && IsLibraryPage(previousPageType)
            && NavFrame.BackStack.Count > 0)
        {
            NavFrame.BackStack.RemoveAt(NavFrame.BackStack.Count - 1);
        }
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (_isBackNavigationInProgress)
            ReleaseBackNavigationAfterRender();

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

    private void ReleaseBackNavigationAfterRender()
    {
        if (_releaseBackNavigationOnNextFrame)
            return;

        _releaseBackNavigationOnNextFrame = true;
        CompositionTarget.Rendering += CompositionTarget_Rendering;
    }

    private void CompositionTarget_Rendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= CompositionTarget_Rendering;
        _releaseBackNavigationOnNextFrame = false;

        if (!DispatcherQueue.TryEnqueue(
                DispatcherQueuePriority.Low,
                CompleteBackNavigation))
        {
            CompleteBackNavigation();
        }
    }

    private void CompleteBackNavigation()
    {
        if (_releaseBackNavigationOnNextFrame)
        {
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
            _releaseBackNavigationOnNextFrame = false;
        }

        _isBackNavigationInProgress = false;
        AppTitleBar.IsBackButtonEnabled = true;
    }

    private static bool IsLibraryPage(Type? pageType) =>
        pageType == typeof(GalleryPage)
        || pageType == typeof(CharacterGalleryPage)
        || pageType == typeof(CoordinateGalleryPage);

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            QueueNavigation(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "library":
                    QueueNavigationToSelectedLibraryPage();
                    break;
                case "screenshots":
                    QueueNavigation(typeof(ScreenshotGalleryPage));
                    break;
                case "videos":
                    QueueNavigation(typeof(VideoGalleryPage));
                    break;
                case "authors" when App.Services.GetRequiredService<AuthorInfoService>().IsAvailable:
                    QueueNavigation(typeof(AuthorsPage));
                    break;
                case "import" when App.Services.GetService<ImportViewModel>() is not null:
                    QueueNavigation(typeof(ImportPage));
                    break;
            }
        }
    }
}

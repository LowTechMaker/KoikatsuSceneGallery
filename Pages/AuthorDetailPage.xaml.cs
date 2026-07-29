using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorDetailPage : Page
{
    public AuthorDetailViewModel ViewModel { get; } = new(
        App.Services.GetService<AuthorPostService>(),
        App.Services.GetRequiredService<GalleryViewModel>(),
        App.Services.GetRequiredService<CharacterGalleryViewModel>(),
        App.Services.GetRequiredService<CoordinateGalleryViewModel>(),
        App.Services.GetRequiredService<ThumbnailCacheService>(),
        App.Services.GetRequiredService<IAppLogger>());

    private const double SceneImageRatio = 135.0 / 240.0;
    private const double CharaImageRatio = 352.0 / 252.0;
    private const double PostImageRatio = 3.0 / 4.0;
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;
    private const double DesiredWidth = 240;
    private const double PostDesiredWidth = 160;
    private const double PostItemSpacing = 8;
    private const double LayoutEpsilon = 0.5;
    private const int ScenesTabIndex = 0;
    private const int CharactersTabIndex = 1;
    private const int CoordinatesTabIndex = 2;
    private const int PostsTabIndex = 3;

    private readonly ThumbnailRequestController _thumbnailRequests;
    private CancellationTokenSource? _postsCts;
    private AuthorDetailNavigationParameter? _navigationParameter;
    private int? _pendingRestoreSelectedTabIndex;
    private bool _isNavigated;
    private bool _postsLoadStarted;

    public AuthorDetailPage()
    {
        _thumbnailRequests = new ThumbnailRequestController(
            App.Services.GetRequiredService<ThumbnailService>(),
            App.Services.GetRequiredService<IAppLogger>(),
            "AuthorDetail.GenerateThumbnail");
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        Loaded += AuthorDetailPage_Loaded;
        TabPivot.SelectionChanged += TabPivot_SelectionChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isNavigated = true;
        _postsLoadStarted = false;
        _thumbnailRequests.Activate();
        if (TryGetNavigationParameter(e.Parameter, out var navigationParameter))
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine(
                $"[AuthorDetail.NavigateTo] "
                + $"{navigationParameter.Summary.Display.Key.ProviderId}:"
                + $"{navigationParameter.Summary.Display.Key.Id}");
#endif
            _navigationParameter = navigationParameter;
            ViewModel.Load(navigationParameter.Summary);
            RestoreSelectedTab(e.NavigationMode);
            TryStartPostLoad();
        }

        ScenesGrid.SizeChanged += Grid_SizeChanged;
        CharactersGrid.SizeChanged += Grid_SizeChanged;
        CoordinatesGrid.SizeChanged += Grid_SizeChanged;

        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyLayout(ScenesGrid, SceneImageRatio);
            ApplyLayout(CharactersGrid, CharaImageRatio);
            ApplyLayout(CoordinatesGrid, CharaImageRatio);
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isNavigated = false;
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[AuthorDetail.NavigateFrom] {ViewModel.Author?.Key.ProviderId}:"
            + $"{ViewModel.Author?.Key.Id}");
#endif
        base.OnNavigatedFrom(e);
        _thumbnailRequests.Cancel();
        _postsCts?.Cancel();
        _postsCts?.Dispose();
        _postsCts = null;
        ViewModel.Unload();
        ScenesGrid.SizeChanged -= Grid_SizeChanged;
        CharactersGrid.SizeChanged -= Grid_SizeChanged;
        CoordinatesGrid.SizeChanged -= Grid_SizeChanged;
    }

    private void TabPivot_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine(
            $"[AuthorDetail.Pivot] SelectedIndex={TabPivot.SelectedIndex}");
#endif
        TryStartPostLoad();
    }

    private void TryStartPostLoad()
    {
        if (!_isNavigated
            || _postsLoadStarted
            || TabPivot.SelectedIndex != PostsTabIndex
            || !ViewModel.CanLoadPosts
            || App.Services.GetService<AuthorPostService>() is not { } postService)
        {
            return;
        }

        _postsLoadStarted = true;
        _postsCts = new CancellationTokenSource();
        ViewModel.LoadPostsAsync(postService, _postsCts.Token)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.LoadPosts");
    }

    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid && WidthChanged(e))
            ApplyLayout(grid, grid == ScenesGrid ? SceneImageRatio : CharaImageRatio);
    }

    private void CardGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        args.RegisterUpdateCallback(CardGrid_Phase1);
    }

    private void CardGrid_Phase1(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is CardBase card)
            _thumbnailRequests.Request(card);
    }

    private static void ApplyLayout(GridView grid, double imageRatio)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (DesiredWidth + CellOverheadW)));
        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * imageRatio;
        double cellH = imageH + FilenameReserve + (CardMargin + CardInset) * 2;

        ApplyItemSize(panel, cellW, cellH);
    }

    private static void ApplyPostImageLayout(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (PostDesiredWidth + PostItemSpacing)));
        double cellW = (available / columns) - PostItemSpacing;
        double cellH = cellW * PostImageRatio;

        ApplyItemSize(panel, cellW, cellH);
    }

    private void PostImagesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid && WidthChanged(e))
            ApplyPostImageLayout(grid);
    }

    private static bool WidthChanged(SizeChangedEventArgs e)
        => Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= LayoutEpsilon;

    private static void ApplyItemSize(ItemsWrapGrid panel, double itemWidth, double itemHeight)
    {
        if (!NearlyEqual(panel.ItemWidth, itemWidth))
            panel.ItemWidth = itemWidth;
        if (!NearlyEqual(panel.ItemHeight, itemHeight))
            panel.ItemHeight = itemHeight;
    }

    private static bool NearlyEqual(double left, double right)
        => !double.IsNaN(left) && Math.Abs(left - right) < LayoutEpsilon;

    public static string FormatCount(int count) => $"({count})";

    public static string FormatFileCount(int count) => count == 1 ? "1 file" : $"{count} files";

    private void GoBack_Click(object sender, RoutedEventArgs e) => App.TryGoBack(Frame);

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.OpenProfile", async () =>
        {
            if (ViewModel.Author is { } author)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(author.ProfileUrl));
        });

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        switch (TabPivot.SelectedIndex)
        {
            case 0 when ViewModel.Scenes.Count > 0:
                SetRestoreSelectedTabOnBack(ScenesTabIndex);
                Frame.Navigate(typeof(DetailPage),
                    ViewModel.Scenes[Random.Shared.Next(ViewModel.Scenes.Count)]);
                break;
            case 1 when ViewModel.Characters.Count > 0:
                SetRestoreSelectedTabOnBack(CharactersTabIndex);
                Frame.Navigate(typeof(CharacterDetailPage),
                    ViewModel.Characters[Random.Shared.Next(ViewModel.Characters.Count)]);
                break;
            case 2 when ViewModel.Coordinates.Count > 0:
                SetRestoreSelectedTabOnBack(CoordinatesTabIndex);
                Frame.Navigate(typeof(CoordinateDetailPage),
                    ViewModel.Coordinates[Random.Shared.Next(ViewModel.Coordinates.Count)]);
                break;
            case 3 when ViewModel.Posts.Count > 0:
                SetRestoreSelectedTabOnBack(PostsTabIndex);
                Frame.Navigate(typeof(PostDetailPage),
                    ViewModel.Posts[Random.Shared.Next(ViewModel.Posts.Count)]);
                break;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.Refresh", async () =>
        {
            if (ViewModel.Author is { } author)
                await App.Services.GetRequiredService<AuthorInfoService>().RefreshAuthorAsync(author.Key);
        });

    private void ScenesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SceneCard card)
        {
            SetRestoreSelectedTabOnBack(ScenesTabIndex);
            Frame.Navigate(typeof(DetailPage), card);
        }
    }

    private void CharactersGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card)
        {
            SetRestoreSelectedTabOnBack(CharactersTabIndex);
            Frame.Navigate(typeof(CharacterDetailPage), card);
        }
    }

    private void CoordinatesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CoordinateCard card)
        {
            SetRestoreSelectedTabOnBack(CoordinatesTabIndex);
            Frame.Navigate(typeof(CoordinateDetailPage), card);
        }
    }

    private void PostTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorPost post })
        {
            SetRestoreSelectedTabOnBack(PostsTabIndex);
            Frame.Navigate(typeof(PostDetailPage), post);
        }
    }

    private void PostImage_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LocalImagePreview preview) return;

        var path = preview.FilePath;
        var scene = App.Services.GetRequiredService<GalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (scene is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(DetailPage), scene); return; }

        var character = App.Services.GetRequiredService<CharacterGalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (character is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(CharacterDetailPage), character); return; }

        var coordinate = App.Services.GetRequiredService<CoordinateGalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (coordinate is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(CoordinateDetailPage), coordinate); return; }
    }

    private void PostImages_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<LocalImagePreview>().Select(p => p.FilePath));

    private void ScenesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<SceneCard>().Select(c => c.FilePath));

    private void CharactersGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<CharacterCard>().Select(c => c.FilePath));

    private void CoordinatesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<CoordinateCard>().Select(c => c.FilePath));

    private static void SetDragFiles(DragItemsStartingEventArgs e, IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return;
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var files = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<IAppLogger>()
                            .LogError("AuthorDetail.PrepareDragFile", ex, path);
                    }
                }
                request.SetData(files);
            }
            finally { deferral.Complete(); }
        });
    }

    private static bool TryGetNavigationParameter(object? parameter, out AuthorDetailNavigationParameter navigationParameter)
    {
        switch (parameter)
        {
            case AuthorDetailNavigationParameter value:
                navigationParameter = value;
                return true;
            case AuthorSummary summary:
                navigationParameter = new AuthorDetailNavigationParameter(summary);
                return true;
            default:
                navigationParameter = null!;
                return false;
        }
    }

    private void SetRestoreSelectedTabOnBack(int tabIndex)
    {
        if (_navigationParameter is not null)
            _navigationParameter.RestoreSelectedTabOnBack = tabIndex;
    }

    private void AuthorDetailPage_Loaded(object sender, RoutedEventArgs e)
    {
        if (_pendingRestoreSelectedTabIndex is not { } tabIndex)
            return;

        _pendingRestoreSelectedTabIndex = null;
        if (tabIndex >= 0 && tabIndex < TabPivot.Items.Count)
            TabPivot.SelectedIndex = tabIndex;
    }

    private void RestoreSelectedTab(NavigationMode navigationMode)
    {
        if (navigationMode == NavigationMode.Back && _navigationParameter?.RestoreSelectedTabOnBack is { } tabIndex)
            _pendingRestoreSelectedTabIndex = tabIndex;

        if (_navigationParameter is not null)
            _navigationParameter.RestoreSelectedTabOnBack = null;
    }
}

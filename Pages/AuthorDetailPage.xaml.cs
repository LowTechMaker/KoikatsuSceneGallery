using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorDetailPage : Page
{
    public AuthorDetailViewModel ViewModel { get; } = new();

    private const double SceneImageRatio = 135.0 / 240.0;
    private const double CharaImageRatio = 352.0 / 252.0;
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;
    private const double DesiredWidth = 240;
    private const int ScenesTabIndex = 0;
    private const int CharactersTabIndex = 1;
    private const int CoordinatesTabIndex = 2;
    private const int PostsTabIndex = 3;

    private CancellationTokenSource? _postsCts;
    private AuthorDetailNavigationParameter? _navigationParameter;

    public AuthorDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (TryGetNavigationParameter(e.Parameter, out var navigationParameter))
        {
            _navigationParameter = navigationParameter;
            ViewModel.Load(navigationParameter.Summary);
            foreach (var card in ViewModel.Scenes)
                App.GalleryViewModel.RequestThumbnail(card);
            foreach (var card in ViewModel.Characters)
                App.CharacterGalleryViewModel.RequestThumbnail(card);
            foreach (var card in ViewModel.Coordinates)
                App.CoordinateGalleryViewModel.RequestThumbnail(card);
            RestoreSelectedTab(e.NavigationMode);
            if (ViewModel.CanLoadPosts && App.AuthorPostService is { } postService)
            {
                _postsCts = new CancellationTokenSource();
                _ = ViewModel.LoadPostsAsync(postService, _postsCts.Token);
            }
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
        base.OnNavigatedFrom(e);
        _postsCts?.Cancel();
        _postsCts?.Dispose();
        _postsCts = null;
        ScenesGrid.SizeChanged -= Grid_SizeChanged;
        CharactersGrid.SizeChanged -= Grid_SizeChanged;
        CoordinatesGrid.SizeChanged -= Grid_SizeChanged;
    }

    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
            ApplyLayout(grid, grid == ScenesGrid ? SceneImageRatio : CharaImageRatio);
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

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    public static string FormatCount(int count) => $"({count})";

    public static string FormatFileCount(int count) => count == 1 ? "1 file" : $"{count} files";

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }

    private async void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Author is { } author)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(author.ProfileUrl));
    }

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

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Author is { } author)
            await App.AuthorInfoService.RefreshAuthorAsync(author.Key);
    }

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

    private void PostsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorPost post)
        {
            SetRestoreSelectedTabOnBack(PostsTabIndex);
            Frame.Navigate(typeof(PostDetailPage), post);
        }
    }

    private async void ScenesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => await SetDragFiles<SceneCard>(e);

    private async void CharactersGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => await SetDragFiles<CharacterCard>(e);

    private async void CoordinatesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => await SetDragFiles<CoordinateCard>(e);

    private static async Task SetDragFiles<T>(DragItemsStartingEventArgs e) where T : class
    {
        var files = new List<StorageFile>();
        foreach (var item in e.Items)
        {
            if (item is T card)
            {
                var path = card switch
                {
                    SceneCard s => s.FilePath,
                    CharacterCard c => c.FilePath,
                    CoordinateCard co => co.FilePath,
                    _ => null,
                };
                if (path is null) continue;
                try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
                catch { }
            }
        }
        if (files.Count > 0)
        {
            e.Data.SetStorageItems(files);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
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

    private void RestoreSelectedTab(NavigationMode navigationMode)
    {
        if (navigationMode == NavigationMode.Back && _navigationParameter?.RestoreSelectedTabOnBack is { } tabIndex)
            TabPivot.SelectedIndex = tabIndex;

        if (_navigationParameter is not null)
            _navigationParameter.RestoreSelectedTabOnBack = null;
    }
}

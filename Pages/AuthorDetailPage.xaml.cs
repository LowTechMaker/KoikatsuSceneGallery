using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorDetailPage : Page
{
    public AuthorDetailViewModel ViewModel { get; } = new();

    private const double ImageRatio = 135.0 / 240.0;
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
    private static int? s_restoreSelectedTabOnBack;

    public AuthorDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AuthorSummary summary)
        {
            ViewModel.Load(summary);
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
            ApplyLayout(ScenesGrid);
            ApplyLayout(CharactersGrid);
            ApplyLayout(CoordinatesGrid);
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
            ApplyLayout(grid);
    }

    private static void ApplyLayout(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (DesiredWidth + CellOverheadW)));
        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * ImageRatio;
        double cellH = imageH + FilenameReserve + (CardMargin + CardInset) * 2;

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    public static string FormatCount(int count) => $"({count})";

    public static string FormatFileCount(int count) => count == 1 ? "1 file" : $"{count} files";

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
                Frame.Navigate(typeof(DetailPage),
                    ViewModel.Scenes[Random.Shared.Next(ViewModel.Scenes.Count)]);
                break;
            case 1 when ViewModel.Characters.Count > 0:
                Frame.Navigate(typeof(CharacterDetailPage),
                    ViewModel.Characters[Random.Shared.Next(ViewModel.Characters.Count)]);
                break;
            case 2 when ViewModel.Coordinates.Count > 0:
                Frame.Navigate(typeof(CoordinateDetailPage),
                    ViewModel.Coordinates[Random.Shared.Next(ViewModel.Coordinates.Count)]);
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
            s_restoreSelectedTabOnBack = ScenesTabIndex;
            Frame.Navigate(typeof(DetailPage), card);
        }
    }

    private void CharactersGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card)
        {
            s_restoreSelectedTabOnBack = CharactersTabIndex;
            Frame.Navigate(typeof(CharacterDetailPage), card);
        }
    }

    private void CoordinatesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CoordinateCard card)
        {
            s_restoreSelectedTabOnBack = CoordinatesTabIndex;
            Frame.Navigate(typeof(CoordinateDetailPage), card);
        }
    }

    private void PostsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AuthorPost post)
        {
            s_restoreSelectedTabOnBack = PostsTabIndex;
            Frame.Navigate(typeof(PostDetailPage), post);
        }
    }

    private void RestoreSelectedTab(NavigationMode navigationMode)
    {
        if (navigationMode == NavigationMode.Back && s_restoreSelectedTabOnBack is { } tabIndex)
            TabPivot.SelectedIndex = tabIndex;

        s_restoreSelectedTabOnBack = null;
    }
}

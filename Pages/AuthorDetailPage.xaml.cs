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

    public AuthorDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AuthorSummary summary)
            ViewModel.Load(summary);

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
            Frame.Navigate(typeof(DetailPage), card);
    }

    private void CharactersGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card)
            Frame.Navigate(typeof(CharacterDetailPage), card);
    }

    private void CoordinatesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CoordinateCard card)
            Frame.Navigate(typeof(CoordinateDetailPage), card);
    }
}

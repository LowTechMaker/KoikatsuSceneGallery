using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CoordinateDetailPage : Page
{
    public CoordinateDetailViewModel ViewModel { get; } = new();

    private static readonly ResourceLoader ResLoader = new();

    public CoordinateDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsReloaded += OnCardsReloaded;
        if (e.Parameter is CoordinateCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsReloaded -= OnCardsReloaded;
    }

    private void OnCardsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Frame.CanGoBack) Frame.GoBack();
        });
    }

    private void ShowCard(CoordinateCard card)
    {
        ViewModel.Card = card;
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920) };
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        UpdateNavigationButtons();
        LoadMetadataAsync(card).Observe(
            App.Services.GetRequiredService<IAppLogger>(),
            "CoordinateDetail.LoadMetadata");
    }

    private async Task LoadMetadataAsync(CoordinateCard card)
    {
        ViewModel.MetadataLoaded = false;
        var meta = await Task.Run(() => CoordinateCardParser.TryParse(card.FilePath));
        if (!ReferenceEquals(ViewModel.Card, card)) return;

        meta ??= new CoordinateMetadata(null);
        ViewModel.CoordinateName = string.IsNullOrWhiteSpace(meta.CoordinateName)
            ? ResLoader.GetString("Common_Unknown")
            : meta.CoordinateName;
        ViewModel.MetadataLoaded = true;
    }

    private void UpdateNavigationButtons()
    {
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        var next = DetailNavigationHelper.Navigate(App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = DetailNavigationHelper.RandomCard(App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private void PreviousCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Navigate(-1);
        args.Handled = true;
    }

    private void NextCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Navigate(1);
        args.Handled = true;
    }

    private void PixivButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CoordinateDetail.OpenPixiv", async () =>
        {
            if (ViewModel.PixivUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void BepisDbButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "CoordinateDetail.OpenBepisDb", async () =>
        {
            if (ViewModel.BepisDbUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void PreviewImage_DragStarting(UIElement sender, DragStartingEventArgs e)
        => DetailNavigationHelper.HandleDragStartingAsync(ViewModel.Card, e)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "CoordinateDetail.PrepareDrag");
}

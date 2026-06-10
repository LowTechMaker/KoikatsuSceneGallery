using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage;

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
        App.CoordinateGalleryViewModel.CardsReloaded += OnCardsReloaded;
        if (e.Parameter is CoordinateCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.CoordinateGalleryViewModel.CardsReloaded -= OnCardsReloaded;
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
        _ = LoadMetadataAsync(card);
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
        var (hasPrev, hasNext) = GetNavigationState();
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private (bool hasPrev, bool hasNext) GetNavigationState()
    {
        var view = App.CoordinateGalleryViewModel.CardsView;
        if (ViewModel.Card == null || view.Count == 0)
            return (false, false);

        var index = view.IndexOf(ViewModel.Card);
        if (index < 0) return (false, false);
        return (index > 0, index < view.Count - 1);
    }

    private void Navigate(int direction)
    {
        var view = App.CoordinateGalleryViewModel.CardsView;
        if (ViewModel.Card == null) return;

        var index = view.IndexOf(ViewModel.Card);
        var newIndex = index + direction;
        if (newIndex >= 0 && newIndex < view.Count && view[newIndex] is CoordinateCard card)
            ShowCard(card);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var view = App.CoordinateGalleryViewModel.CardsView;
        if (view.Count == 0) return;

        var currentIndex = ViewModel.Card != null ? view.IndexOf(ViewModel.Card) : -1;
        var newIndex = Random.Shared.Next(view.Count);
        if (view.Count > 1)
        {
            while (newIndex == currentIndex)
                newIndex = Random.Shared.Next(view.Count);
        }

        if (view[newIndex] is CoordinateCard card)
            ShowCard(card);
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

    private async void PixivButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PixivArtworkId is { } id)
            await Windows.System.Launcher.LaunchUriAsync(new Uri($"https://www.pixiv.net/artworks/{id}"));
    }

    private async void BepisDbButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BepisDbUrl is { } url)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void PreviewImage_DragStarting(UIElement sender, DragStartingEventArgs e)
    {
        if (ViewModel.Card is { } card)
        {
            var deferral = e.GetDeferral();
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
                e.Data.SetStorageItems([file]);
                e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}

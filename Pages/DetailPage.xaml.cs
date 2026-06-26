using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class DetailPage : Page
{
    public DetailViewModel ViewModel { get; } = new();

    public DetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.GalleryViewModel.CardRemovedNotification += OnCardRemoved;
        App.GalleryViewModel.CardsReloaded += OnCardsReloaded;
        if (e.Parameter is SceneCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.GalleryViewModel.CardRemovedNotification -= OnCardRemoved;
        App.GalleryViewModel.CardsReloaded -= OnCardsReloaded;
    }

    private void OnCardRemoved(string path)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Card == null || !string.Equals(ViewModel.Card.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return;

            var next = DetailNavigationHelper.FindAdjacentOnRemoval(App.GalleryViewModel.CardsView, ViewModel.Card);
            if (next != null)
                ShowCard(next);
            else if (Frame.CanGoBack)
                Frame.GoBack();
        });
    }

    private void OnCardsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Frame.CanGoBack) Frame.GoBack();
        });
    }

    private void ShowCard(SceneCard card)
    {
        ViewModel.Card = card;
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920) };
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(App.GalleryViewModel.CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        var next = DetailNavigationHelper.Navigate(App.GalleryViewModel.CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = DetailNavigationHelper.RandomCard(App.GalleryViewModel.CardsView, ViewModel.Card);
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

    private async void PixivButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PixivUrl is { } url)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private async void BepisDbButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.BepisDbUrl is { } url)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
    }

    private void PixivButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.PixivUrl is { } url)
        {
            DetailNavigationHelper.CopyText(url);
            e.Handled = true;
        }
    }

    private void BepisDbButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.BepisDbUrl is { } url)
        {
            DetailNavigationHelper.CopyText(url);
            e.Handled = true;
        }
    }

    private void FilePath_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.Card is { } card)
        {
            DetailNavigationHelper.CopyText(card.FilePath);
            e.Handled = true;
        }
    }

    private void FilePath_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.Card is { } card && Path.GetDirectoryName(card.FilePath) is { } folder)
        {
            DetailNavigationHelper.CopyText(folder);
            e.Handled = true;
        }
    }

    private void Author_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.AuthorSummary is { } summary)
            Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(summary));
    }

    private void SiblingCard_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SceneCard card)
            ShowCard(card);
    }

    private void PreviewImage_DragStarting(UIElement sender, DragStartingEventArgs e)
        => DetailNavigationHelper.HandleDragStarting(ViewModel.Card, e);
}

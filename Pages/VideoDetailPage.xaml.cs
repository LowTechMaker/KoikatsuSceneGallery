using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class VideoDetailPage : Page
{
    public MediaDetailViewModel ViewModel { get; } = new();

    private int _rotationDegrees;

    public VideoDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.VideoGalleryViewModel.CardRemovedNotification += OnCardRemoved;
        App.VideoGalleryViewModel.CardsReloaded += OnCardsReloaded;
        if (e.Parameter is MediaCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.VideoGalleryViewModel.CardRemovedNotification -= OnCardRemoved;
        App.VideoGalleryViewModel.CardsReloaded -= OnCardsReloaded;
        DisposePlayer();
    }

    private void DisposePlayer()
    {
        if (VideoPlayer.MediaPlayer is { } player)
        {
            player.Pause();
            VideoPlayer.SetMediaPlayer(null);
            player.Dispose();
        }
    }

    private void OnCardRemoved(string path)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Card == null || !string.Equals(ViewModel.Card.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return;

            var next = DetailNavigationHelper.FindAdjacentOnRemoval(App.VideoGalleryViewModel.CardsView, ViewModel.Card);
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

    private void ShowCard(MediaCard card)
    {
        ViewModel.Card = card;

        DisposePlayer();
        var player = new MediaPlayer
        {
            Source = MediaSource.CreateFromUri(card.FileUri),
            AutoPlay = true
        };
        VideoPlayer.SetMediaPlayer(player);

        _rotationDegrees = 0;
        VideoTransform.Rotation = 0;
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(App.VideoGalleryViewModel.CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        var next = DetailNavigationHelper.Navigate(App.VideoGalleryViewModel.CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = DetailNavigationHelper.RandomCard(App.VideoGalleryViewModel.CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => Rotate(90);

    private void Rotate(int delta)
    {
        _rotationDegrees = (_rotationDegrees + delta + 360) % 360;
        VideoTransform.Rotation = _rotationDegrees;
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

    private void RotateRight_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        Rotate(90);
        args.Handled = true;
    }
}

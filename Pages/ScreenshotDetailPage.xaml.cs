using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ScreenshotDetailPage : Page
{
    public MediaDetailViewModel ViewModel { get; } = new();

    private int _rotationDegrees;

    public ScreenshotDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        App.ScreenshotGalleryViewModel.CardRemovedNotification += OnCardRemoved;
        App.ScreenshotGalleryViewModel.CardsReloaded += OnCardsReloaded;
        if (e.Parameter is MediaCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.ScreenshotGalleryViewModel.CardRemovedNotification -= OnCardRemoved;
        App.ScreenshotGalleryViewModel.CardsReloaded -= OnCardsReloaded;
    }

    private void OnCardRemoved(string path)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Card == null || !string.Equals(ViewModel.Card.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return;

            var next = DetailNavigationHelper.FindAdjacentOnRemoval(App.ScreenshotGalleryViewModel.CardsView, ViewModel.Card);
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
        var bitmap = new BitmapImage();
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        _rotationDegrees = 0;
        ImageTransform.Rotation = 0;
        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(App.ScreenshotGalleryViewModel.CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        var next = DetailNavigationHelper.Navigate(App.ScreenshotGalleryViewModel.CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = DetailNavigationHelper.RandomCard(App.ScreenshotGalleryViewModel.CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private void RotateLeftButton_Click(object sender, RoutedEventArgs e) => Rotate(-90);
    private void RotateRightButton_Click(object sender, RoutedEventArgs e) => Rotate(90);

    private void Rotate(int delta)
    {
        _rotationDegrees = (_rotationDegrees + delta + 360) % 360;
        ImageTransform.Rotation = _rotationDegrees;
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

    private void PreviewImage_DragStarting(UIElement sender, DragStartingEventArgs e)
        => DetailNavigationHelper.HandleDragStarting(ViewModel.Card, e);
}

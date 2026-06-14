using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

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

            var view = App.ScreenshotGalleryViewModel.CardsView;
            var index = view.IndexOf(ViewModel.Card);
            if (index >= 0 && index < view.Count - 1 && view[index + 1] is MediaCard next)
                ShowCard(next);
            else if (index > 0 && view[index - 1] is MediaCard prev)
                ShowCard(prev);
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
        var (hasPrev, hasNext) = GetNavigationState();
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private (bool hasPrev, bool hasNext) GetNavigationState()
    {
        var view = App.ScreenshotGalleryViewModel.CardsView;
        if (ViewModel.Card == null || view.Count == 0)
            return (false, false);

        var index = view.IndexOf(ViewModel.Card);
        if (index < 0) return (false, false);
        return (index > 0, index < view.Count - 1);
    }

    private void Navigate(int direction)
    {
        var view = App.ScreenshotGalleryViewModel.CardsView;
        if (ViewModel.Card == null) return;

        var index = view.IndexOf(ViewModel.Card);
        var newIndex = index + direction;
        if (newIndex >= 0 && newIndex < view.Count && view[newIndex] is MediaCard card)
            ShowCard(card);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var view = App.ScreenshotGalleryViewModel.CardsView;
        if (view.Count == 0) return;

        var currentIndex = ViewModel.Card != null ? view.IndexOf(ViewModel.Card) : -1;
        var newIndex = Random.Shared.Next(view.Count);
        if (view.Count > 1)
        {
            while (newIndex == currentIndex)
                newIndex = Random.Shared.Next(view.Count);
        }

        if (view[newIndex] is MediaCard card)
            ShowCard(card);
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

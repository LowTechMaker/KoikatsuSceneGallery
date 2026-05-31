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

public sealed partial class CharacterDetailPage : Page
{
    public CharacterDetailViewModel ViewModel { get; } = new();

    private static readonly ResourceLoader ResLoader = new();

    public CharacterDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is CharacterCard card)
            ShowCard(card);
    }

    private void ShowCard(CharacterCard card)
    {
        ViewModel.Card = card;
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920) };
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        UpdateNavigationButtons();
        _ = LoadMetadataAsync(card);
    }

    /// <summary>
    /// Parses the opened card's embedded metadata off the UI thread and applies
    /// it, unless the user has already navigated to a different card.
    /// </summary>
    private async Task LoadMetadataAsync(CharacterCard card)
    {
        ViewModel.MetadataLoaded = false;
        var meta = await Task.Run(() => CharacterCardParser.TryParse(card.FilePath));
        if (!ReferenceEquals(ViewModel.Card, card)) return; // navigated away while parsing

        meta ??= new CharacterMetadata(null, null, null, -1, GameVersion.Unknown, false);
        ViewModel.FullName = string.IsNullOrWhiteSpace(meta.FullName)
            ? ResLoader.GetString("Common_Unknown")
            : meta.FullName;
        ViewModel.Nickname = meta.Nickname ?? string.Empty;
        ViewModel.SexDisplay = meta.Sex switch
        {
            CharacterMetadata.SexMale => ResLoader.GetString("Common_Male"),
            CharacterMetadata.SexFemale => ResLoader.GetString("Common_Female"),
            _ => ResLoader.GetString("Common_Unknown")
        };
        ViewModel.GameDisplay = meta.Game switch
        {
            GameVersion.Koikatsu => "Koikatsu",
            GameVersion.KoikatsuSunshine => "Koikatsu Sunshine",
            _ => ResLoader.GetString("Common_Unknown")
        };
        ViewModel.MadevilDisplay = meta.IsMadevil
            ? ResLoader.GetString("Common_Yes")
            : ResLoader.GetString("Common_No");
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
        var view = App.CharacterGalleryViewModel.CardsView;
        if (ViewModel.Card == null || view.Count == 0)
            return (false, false);

        var index = view.IndexOf(ViewModel.Card);
        if (index < 0) return (false, false);
        return (index > 0, index < view.Count - 1);
    }

    private void Navigate(int direction)
    {
        var view = App.CharacterGalleryViewModel.CardsView;
        if (ViewModel.Card == null) return;

        var index = view.IndexOf(ViewModel.Card);
        var newIndex = index + direction;
        if (newIndex >= 0 && newIndex < view.Count && view[newIndex] is CharacterCard card)
            ShowCard(card);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var view = App.CharacterGalleryViewModel.CardsView;
        if (view.Count == 0) return;

        var currentIndex = ViewModel.Card != null ? view.IndexOf(ViewModel.Card) : -1;
        var newIndex = Random.Shared.Next(view.Count);
        if (view.Count > 1)
        {
            while (newIndex == currentIndex)
                newIndex = Random.Shared.Next(view.Count);
        }

        if (view[newIndex] is CharacterCard card)
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

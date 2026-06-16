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
        App.CharacterGalleryViewModel.VersionIndexChanged += OnVersionIndexChanged;
        App.CharacterGalleryViewModel.CardsReloaded += OnCardsReloaded;
        if (e.Parameter is CharacterCard card)
            ShowCard(card);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.CharacterGalleryViewModel.VersionIndexChanged -= OnVersionIndexChanged;
        App.CharacterGalleryViewModel.CardsReloaded -= OnCardsReloaded;
    }

    private void OnVersionIndexChanged(string characterName)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Card == null || !ViewModel.MetadataLoaded) return;
            if (!string.Equals(ViewModel.FullName, characterName, StringComparison.Ordinal)) return;

            var versions = App.CharacterGalleryViewModel.GetVersions(characterName);
            if (versions != null && versions.Contains(ViewModel.Card))
            {
                LoadVersions(ViewModel.Card, characterName);
            }
            else if (versions != null && versions.Count > 0)
            {
                ShowCard(versions[0]);
            }
            else
            {
                if (Frame.CanGoBack) Frame.GoBack();
            }
        });
    }

    private void OnCardsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Frame.CanGoBack) Frame.GoBack();
        });
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

        LoadVersions(card, meta.FullName);
    }

    private void LoadVersions(CharacterCard card, string fullName)
    {
        var versions = App.CharacterGalleryViewModel.GetVersions(fullName);
        if (versions != null && versions.Count > 1)
        {
            ViewModel.Versions = new System.Collections.ObjectModel.ObservableCollection<CharacterCard>(versions);
            ViewModel.HasMultipleVersions = true;
            ViewModel.TotalVersions = versions.Count;
            ViewModel.VersionIndex = versions.IndexOf(card) + 1;
            foreach (var v in versions)
                App.CharacterGalleryViewModel.RequestThumbnail(v);
        }
        else
        {
            ViewModel.Versions = null;
            ViewModel.HasMultipleVersions = false;
            ViewModel.VersionIndex = 0;
            ViewModel.TotalVersions = 0;
        }
    }

    private void VersionItem_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card && !ReferenceEquals(card, ViewModel.Card))
            ShowCard(card);
    }

    public static string FormatTimestamp(CharacterCard card) =>
        card.FileTimestamp.ToString("yyyy-MM-dd HH:mm:ss");

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

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
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

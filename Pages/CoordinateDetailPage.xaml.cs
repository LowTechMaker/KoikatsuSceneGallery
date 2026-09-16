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
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class CoordinateDetailPage : Page
{
    private readonly ViewerChrome _viewer;
    public CoordinateDetailViewModel ViewModel { get; } = new();

    private static readonly ResourceLoader ResLoader = new();
    private CancellationTokenSource? _metadataCts;
    private AuthorKey? _authorScope;

    public CoordinateDetailPage()
    {
        InitializeComponent();
        _viewer = new(this, PreviewImage, () => ViewModel.Card, card => ShowCard((CoordinateCard)card));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewer.SetContext(BrowseContexts.From(e.Parameter));
        App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsReloaded += OnCardsReloaded;
        switch (e.Parameter)
        {
            case BrowseNavigation navigation:
                ShowCard((CoordinateCard)navigation.Card);
                break;
            case AuthorScopedCoordinateNavigationParameter scoped:
                _authorScope = scoped.AuthorKey;
                ShowCard(scoped.Card);
                break;
            case CoordinateCard card:
                _authorScope = null;
                ShowCard(card);
                break;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        PageCancellation.Stop(ref _metadataCts);
        App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsReloaded -= OnCardsReloaded;
    }

    private void OnCardsReloaded()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Frame?.Content, this) || ViewModel.Card is not { } current)
                return;

            DetailNavigationHelper.RefreshAfterReload(
                App.Services.GetRequiredService<CoordinateGalleryViewModel>().Cards, current,
                ShowCard,
                UpdateNavigationButtons,
                () => { if (Frame.CanGoBack) Frame.GoBack(); });
        });
    }

    private void ShowCard(CoordinateCard card)
    {
        var metadataCts = PageCancellation.Restart(ref _metadataCts);
        ViewModel.Card = card;
        var bitmap = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920) };
        bitmap.UriSource = card.FileUri;
        PreviewImage.Source = bitmap;
        UpdateNavigationButtons();
        LoadMetadataAsync(card, metadataCts.Token).Observe(
            App.Services.GetRequiredService<IAppLogger>(),
            "CoordinateDetail.LoadMetadata");
    }

    private async Task LoadMetadataAsync(CoordinateCard card, CancellationToken cancellationToken)
    {
        ViewModel.MetadataLoaded = false;
        var meta = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = CoordinateCardParser.TryParse(card.FilePath);
            cancellationToken.ThrowIfCancellationRequested();
            return parsed;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(ViewModel.Card, card)) return;

        meta ??= new CoordinateMetadata(null);
        ViewModel.CoordinateName = string.IsNullOrWhiteSpace(meta.CoordinateName)
            ? ResLoader.GetString("Common_Unknown")
            : meta.CoordinateName;
        ViewModel.MetadataLoaded = true;
    }

    private void UpdateNavigationButtons()
    {
        if (_viewer.Update()) return;
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(
            GetScopedCards(), App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        if (_viewer.Navigate(direction)) return;
        var next = DetailNavigationHelper.Navigate(
            GetScopedCards(), App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }
    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewer.Navigate(0, true)) return;
        var card = DetailNavigationHelper.RandomCard(
            GetScopedCards(), App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private List<CoordinateCard>? GetScopedCards() => _authorScope is not { } author
        ? null
        : App.Services.GetRequiredService<CoordinateGalleryViewModel>().CardsView
            .OfType<CoordinateCard>()
            .Where(card => card.Author?.Key == author)
            .ToList();

    private void PreviousCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot) || KoikatsuSceneGallery.Controls.MetadataPanel.ContainsFocus(XamlRoot)) return;
        Navigate(-1);
        args.Handled = true;
    }

    private void NextCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot) || KoikatsuSceneGallery.Controls.MetadataPanel.ContainsFocus(XamlRoot)) return;
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

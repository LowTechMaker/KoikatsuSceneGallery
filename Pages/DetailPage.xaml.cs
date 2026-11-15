using KoikatsuSceneGallery.Controls;
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
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class DetailPage : Page
{
    private readonly ViewerChrome _viewer;
    public DetailViewModel ViewModel { get; } = new(
        App.Services.GetRequiredService<AuthorInfoService>(),
        App.Services.GetRequiredService<GalleryViewModel>());

    private readonly UserCardRatingStore _ratings = App.Services.GetRequiredService<UserCardRatingStore>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();
    private readonly UserRatingPicker _ratingPicker = new(large: false);
    private string? _ratingHash;
    // Bumped per card shown, so a hash finishing late cannot label the next card.
    private int _ratingVersion;

    private readonly ProgressivePreview _preview;

    public DetailPage()
    {
        InitializeComponent();
        _preview = new ProgressivePreview(PreviewImage);
        _ratingPicker.AddNumberKeys();
        BackNavigation.EscapeGoesBack(this);
        _viewer = new(this, PreviewImage, () => ViewModel.Card, card => ShowCard((SceneCard)card));
        UserRatingHost.Child = _ratingPicker;
        _ratingPicker.RatingChosen += rating => UiEventGuard.Run(_logger, "Detail.SetUserRating", () => SetUserRatingAsync(rating));
    }

    private AuthorKey? _authorScope;
    private HashSet<string>? _groupScope;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _viewer.SetContext(BrowseContexts.From(e.Parameter));
        App.Services.GetRequiredService<GalleryViewModel>().CardRemovedNotification += OnCardRemoved;
        App.Services.GetRequiredService<GalleryViewModel>().CardsReloaded += OnCardsReloaded;
        switch (e.Parameter)
        {
            case BrowseNavigation navigation:
                _groupScope = navigation.Context.ShowRelatedImages ? navigation.Context.Cards.Select(c => c.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
                ViewModel.GroupScope = _groupScope;
                ShowCard((SceneCard)navigation.Card);
                break;
            case GroupScopedSceneNavigationParameter group:
                _authorScope = null;
                _groupScope = group.Cards.Select(c => c.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                ViewModel.GroupScope = _groupScope;
                ShowCard(group.Card);
                break;
            case AuthorScopedSceneNavigationParameter scoped:
                _groupScope = null;
                ViewModel.GroupScope = null;
                _authorScope = scoped.AuthorKey;
                ShowCard(scoped.Card);
                break;
            case SceneCard card:
                _groupScope = null;
                ViewModel.GroupScope = null;
                _authorScope = null;
                ShowCard(card);
                break;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Services.GetRequiredService<GalleryViewModel>().CardRemovedNotification -= OnCardRemoved;
        App.Services.GetRequiredService<GalleryViewModel>().CardsReloaded -= OnCardsReloaded;
    }

    private void OnCardRemoved(string path)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(Frame?.Content, this)) return;
            if (_groupScope is not null)
            {
                ViewModel.RefreshSiblingCards();
                UpdateNavigationButtons();
            }
            if (ViewModel.Card == null || !string.Equals(ViewModel.Card.FilePath, path, StringComparison.OrdinalIgnoreCase))
                return;

            if (_viewer.RecoverIfMissing()) return;
            var scoped = GetScopedCards();
            var next = scoped is null
                ? DetailNavigationHelper.FindAdjacentOnRemoval(App.Services.GetRequiredService<GalleryViewModel>().CardsView, ViewModel.Card)
                : scoped.FirstOrDefault();
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
            // A gallery reload can complete just after this page was opened from
            // an author/post image. Keep the detail page when its card still
            // exists; the previous unconditional GoBack made that navigation
            // appear to do nothing and could pop an unrelated page later.
            if (!ReferenceEquals(Frame?.Content, this) || ViewModel.Card is not { } current)
                return;

            DetailNavigationHelper.RefreshAfterReload(
                App.Services.GetRequiredService<GalleryViewModel>().Cards, current,
                ShowCard,
                () =>
                {
                    if (_groupScope is not null) ViewModel.RefreshSiblingCards();
                    UpdateNavigationButtons();
                },
                () => { if (Frame.CanGoBack) Frame.GoBack(); });
        });
    }

    private void ShowCard(SceneCard card)
    {
        ViewModel.Card = card;
        _preview.Show(card, card.Width);
        UpdateNavigationButtons();
        UiEventGuard.Run(_logger, "Detail.LoadUserRating", () => LoadUserRatingAsync(card));
    }

    private async Task LoadUserRatingAsync(SceneCard card)
    {
        var version = ++_ratingVersion;
        _ratingHash = null;
        _ratingPicker.SetEnabled(false);
        ShowUserRating(null);
        var roots = App.Services.GetRequiredService<SettingsViewModel>().FolderPaths.ToArray();
        var hash = await Task.Run(() =>
        {
            _ratings.EnsureLoaded(roots);
            return CardPayloadHash.TryCompute(card.FilePath);
        });
        if (version != _ratingVersion) return;
        if (hash is null)
        {
            UserRatingStatus.Text = UiText.Get("Detail_UserRatingUnavailable");
            return;
        }

        var rating = await _ratings.ResolveAsync(card.FilePath, hash);
        if (version != _ratingVersion) return;
        _ratingHash = hash;
        _ratingPicker.SetEnabled(true);
        ShowUserRating(rating);
    }

    private async Task SetUserRatingAsync(int rating)
    {
        if (ViewModel.Card is not { } card || _ratingHash is not { } hash) return;
        var version = _ratingVersion;
        var saved = await _ratings.SetAsync(card.FilePath, hash, rating);
        if (version != _ratingVersion) return;
        ShowUserRating(saved ? rating : _ratings.TryGetByHash(hash));
        if (!saved) AppToast.Show(UiText.Get("Rating_SaveFailed"), InfoBarSeverity.Error, key: "rating-save");
    }

    private void ClearUserRating_Click(object sender, RoutedEventArgs e) =>
        UiEventGuard.Run(_logger, "Detail.ClearUserRating", async () =>
        {
            if (ViewModel.Card is not { } card || _ratingHash is not { } hash) return;
            var version = _ratingVersion;
            var cleared = await _ratings.ClearAsync(card.FilePath, hash);
            if (version != _ratingVersion) return;
            ShowUserRating(_ratings.TryGetByHash(hash));
            if (!cleared) AppToast.Show(UiText.Get("Rating_SaveFailed"), InfoBarSeverity.Error, key: "rating-save");
        });

    private void ShowUserRating(int? rating)
    {
        _ratingPicker.Rating = rating;
        ClearUserRatingButton.Visibility = rating is null ? Visibility.Collapsed : Visibility.Visible;
        UserRatingStatus.Text = rating is null && _ratingHash is not null ? UiText.Get("Detail_UserRatingNone") : "";
    }

    private void UpdateNavigationButtons()
    {
        if (_viewer.Update()) return;
        var (hasPrev, hasNext) = DetailNavigationHelper.GetNavigationState(
            GetScopedCards(), App.Services.GetRequiredService<GalleryViewModel>().CardsView, ViewModel.Card);
        PrevButton.IsEnabled = hasPrev;
        NextButton.IsEnabled = hasNext;
    }

    private void Navigate(int direction)
    {
        if (_viewer.Navigate(direction)) return;
        var next = DetailNavigationHelper.Navigate(
            GetScopedCards(), App.Services.GetRequiredService<GalleryViewModel>().CardsView, ViewModel.Card, direction);
        if (next != null) ShowCard(next);
    }

    private void PrevButton_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void NextButton_Click(object sender, RoutedEventArgs e) => Navigate(1);

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewer.Navigate(0, true)) return;
        var card = DetailNavigationHelper.RandomCard(
            GetScopedCards(), App.Services.GetRequiredService<GalleryViewModel>().CardsView, ViewModel.Card);
        if (card != null) ShowCard(card);
    }

    private List<SceneCard>? GetScopedCards()
    {
        var cards = App.Services.GetRequiredService<GalleryViewModel>().CardsView.OfType<SceneCard>();
        if (_groupScope is { } group) return cards.Where(c => group.Contains(c.FilePath)).ToList();
        return _authorScope is { } author ? cards.Where(c => c.Author?.Key == author).ToList() : null;
    }

    private void PreviousCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot)) return;
        Navigate(-1);
        args.Handled = true;
    }

    private void NextCard_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewerChrome.IsEditing(XamlRoot)) return;
        Navigate(1);
        args.Handled = true;
    }

    private void PixivButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "SceneDetail.OpenPixiv", async () =>
        {
            if (ViewModel.PixivUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void BepisDbButton_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "SceneDetail.OpenBepisDb", async () =>
        {
            if (ViewModel.BepisDbUrl is { } url)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
        });

    private void PixivButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.PixivUrl is { } url)
        {
            DetailNavigationHelper.CopyText(url);
            RecordUse("Detail.CopyPixivUrl");
            e.Handled = true;
        }
    }

    private void BepisDbButton_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (ViewModel.BepisDbUrl is { } url)
        {
            DetailNavigationHelper.CopyText(url);
            RecordUse("Detail.CopyBepisDbUrl");
            e.Handled = true;
        }
    }

    private void FilePath_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (ViewModel.Card is { } card)
        {
            DetailNavigationHelper.CopyText(card.FilePath);
            RecordUse("Detail.CopyFilePath");
            e.Handled = true;
        }
    }

    /// <summary>
    /// Copying a card's path or origin link counts as taking it somewhere, which
    /// is what record mode is for. The service decides whether to keep it.
    /// </summary>
    private void RecordUse(string operation)
    {
        if (ViewModel.Card is { } card)
            App.Services.GetService<UsageRecordService>()?.Record([card.FilePath], operation);
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
        => DetailNavigationHelper.HandleDragStartingAsync(ViewModel.Card, e)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "SceneDetail.PrepareDrag");
}

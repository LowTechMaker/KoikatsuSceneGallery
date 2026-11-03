using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// Rates the scene library's unrated cards one at a time with the user's own
/// rating, kept apart from the platform's (see <see cref="UserCardRatingStore"/>).
/// </summary>
public sealed partial class CardRatingPage : Page
{
    private readonly GalleryViewModel _gallery = App.Services.GetRequiredService<GalleryViewModel>();
    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();
    private readonly UserCardRatingStore _ratings = App.Services.GetRequiredService<UserCardRatingStore>();
    private readonly IAppLogger _logger = App.Services.GetRequiredService<IAppLogger>();
    private readonly UserRatingPicker _picker = new(large: true);
    private readonly LinkedList<SceneCard> _queue = new();
    private readonly Stack<(SceneCard Card, string Hash)> _history = new();
    private (SceneCard Card, string Hash)? _current;
    // Bumped whenever the queue is rebuilt or the page is left, so a hash that
    // finishes computing afterwards cannot put a stale card on screen.
    private int _version;
    private bool _active, _busy;

    public CardRatingPage()
    {
        InitializeComponent();
        PickerHost.Child = _picker;
        _picker.AddNumberKeys();
        _picker.RatingChosen += rating => UiEventGuard.Run(_logger, "Rating.Set", () => RateAsync(rating));
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _gallery.CardsReloaded += OnCardsReloaded;
        UiEventGuard.Run(_logger, "Rating.Start", StartAsync);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _version++;
        _gallery.CardsReloaded -= OnCardsReloaded;
        base.OnNavigatedFrom(e);
    }

    private void OnCardsReloaded() => DispatcherQueue.TryEnqueue(() =>
    {
        // A rescan while a card is on screen must not yank it away mid-decision;
        // the new cards are picked up on the next visit.
        if (_active && _current is null) UiEventGuard.Run(_logger, "Rating.Restart", StartAsync);
    });

    private async Task StartAsync()
    {
        var version = ++_version;
        ShowStatus(busy: true, UiText.Get("Rating_Loading"), "");
        if (_gallery.Cards.Count == 0 && !_gallery.IsLoading)
            await _gallery.LoadCardsCommand.ExecuteAsync(null);
        // Still scanning: CardsReloaded starts again once the cards are in.
        if (version != _version || _gallery.IsLoading) return;

        var roots = _settings.FolderPaths.ToArray();
        await Task.Run(() => _ratings.EnsureLoaded(roots));
        if (version != _version) return;

        _history.Clear();
        _current = null;
        _queue.Clear();
        foreach (var card in _gallery.Cards
                     .Where(card => _ratings.TryGetByPath(card.FilePath) is null)
                     .OrderBy(card => card.FilePath, StringComparer.CurrentCultureIgnoreCase))
            _queue.AddLast(card);
        await ShowNextAsync(version);
    }

    /// <summary>
    /// Shows the next card that is still unrated. A card rated under another
    /// path (moved or renamed) is only recognised by its hash, so it is skipped
    /// here, and its stored path is brought up to date on the way.
    /// </summary>
    private async Task ShowNextAsync(int version)
    {
        while (_queue.First is { } node)
        {
            _queue.RemoveFirst();
            var card = node.Value;
            var hash = await Task.Run(() => CardPayloadHash.TryCompute(card.FilePath));
            if (version != _version) return;
            if (hash is null) continue;
            var known = await _ratings.ResolveAsync(card.FilePath, hash);
            if (version != _version) return;
            if (known is not null) continue;
            Show(card, hash, null);
            return;
        }

        _current = null;
        if (_gallery.Cards.Count == 0)
            ShowStatus(busy: false, UiText.Get("Rating_NoCardsTitle"), UiText.Get("Rating_NoCardsDescription"));
        else
            ShowStatus(busy: false, UiText.Get("Rating_DoneTitle"), UiText.Format("Rating_DoneDescription", _ratings.Count));
    }

    private async Task RateAsync(int rating)
    {
        if (_current is not { } current || _busy) return;
        _busy = true;
        try
        {
            if (!await _ratings.SetAsync(current.Card.FilePath, current.Hash, rating))
            {
                _picker.Rating = _ratings.TryGetByHash(current.Hash);
                ProgressText.Text = UiText.Get("Rating_SaveFailed");
                return;
            }

            _history.Push(current);
            await ShowNextAsync(_version);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _history.Count == 0) return;
        if (_current is { } current && _ratings.TryGetByHash(current.Hash) is null)
            _queue.AddFirst(current.Card);
        var previous = _history.Pop();
        Show(previous.Card, previous.Hash, _ratings.TryGetByHash(previous.Hash));
    }

    private void Show(SceneCard card, string hash, int? rating)
    {
        _current = (card, hash);
        CardImage.Source = new BitmapImage { DecodePixelWidth = Math.Min(card.Width, 1920), UriSource = card.FileUri };
        FileNameText.Text = card.FileName;
        AuthorText.Text = card.Author?.Name ?? "";
        AuthorText.Visibility = card.Author is null ? Visibility.Collapsed : Visibility.Visible;
        _picker.Rating = rating;
        PreviousButton.IsEnabled = _history.Count > 0;
        CardPanel.Visibility = Visibility.Visible;
        StatusPanel.Visibility = Visibility.Collapsed;
        BusyRing.IsActive = false;
        ProgressText.Text = UiText.Format("Rating_Progress", _queue.Count + (rating is null ? 1 : 0), _ratings.Count);
    }

    private void ShowStatus(bool busy, string title, string description)
    {
        CardPanel.Visibility = Visibility.Collapsed;
        CardImage.Source = null;
        StatusPanel.Visibility = Visibility.Visible;
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StatusTitle.Text = title;
        StatusDescription.Text = description;
        StatusDescription.Visibility = description.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        StatusPreviousButton.Visibility = !busy && _history.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProgressText.Text = busy ? "" : UiText.Format("Rating_RatedCount", _ratings.Count);
    }
}

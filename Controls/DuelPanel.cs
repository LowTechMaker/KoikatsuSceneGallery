using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// "This or that" at the top of the home page: two large cards; the one the
/// user picks goes into "want to try" and a new pair comes at once.
/// </summary>
/// <remarks>
/// A game that fills the queue, so it must feel instant:
/// <list type="bullet">
/// <item>the next pair is chosen one step ahead (<see cref="HomeViewModel.DuelPairs"/>)
/// and its pictures are decoded while the current pair is on screen;</item>
/// <item>a pick moves on first and saves the queue flag after, through
/// <see cref="CardStateActions"/> so the toast and the shelf update are the
/// same as everywhere else;</item>
/// <item>the large picture is the card file itself decoded at the width it is
/// shown (<see cref="BitmapSource.SetSourceAsync"/>, off the UI thread), with
/// the small gallery thumbnail standing in until it is ready — the thumbnails
/// alone are too small to be sharp at this size.</item>
/// </list>
/// The card not picked is only not shown again this session; it is not hidden
/// or marked. The session lives in <see cref="HomeViewModel"/>, which outlives
/// this cached page.
/// </remarks>
internal sealed class DuelPanel : StackPanel
{
    /// <summary>The two cards together are never wider than this.</summary>
    private const double MaxWidthBoth = 1100;
    /// <summary>Below this the two cards stack.</summary>
    private const double StackBelow = 640;
    /// <summary>The decoded width: a card is at most half of <see cref="MaxWidthBoth"/>, at 2x scale.</summary>
    private const int DecodeWidth = 1100;

    private readonly HomeViewModel? _home = App.Services.GetService<HomeViewModel>();
    private readonly TextBlock _count = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        Visibility = Visibility.Collapsed,
    };
    private readonly Grid _cards = new() { ColumnSpacing = 16, RowSpacing = 16, MaxWidth = MaxWidthBoth, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly DuelCardView _left = new("HomeDuel_Left");
    private readonly DuelCardView _right = new("HomeDuel_Right");
    private readonly Button _skip = new() { Content = UiText.Get("Home_DuelSkip") };
    private readonly TextBlock _empty = new()
    {
        Text = UiText.Get("Home_DuelEmpty"),
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        TextWrapping = TextWrapping.Wrap,
        Visibility = Visibility.Collapsed,
    };
    private readonly DecodedPictures _pictures = new(DecodeWidth);
    private bool _stacked;

    public DuelPanel()
    {
        Spacing = 8;
        AutomationProperties.SetAutomationId(this, "HomeDuel");
        AutomationProperties.SetName(this, UiText.Get("Home_DuelTitle"));
        AutomationProperties.SetAutomationId(_count, "HomeDuel_Count");
        AutomationProperties.SetAutomationId(_skip, "HomeDuel_Skip");

        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(new TextBlock
        {
            Text = UiText.Get("Home_DuelTitle"),
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        heading.Children.Add(_count);
        Children.Add(heading);
        Children.Add(new TextBlock
        {
            Text = UiText.Get("Home_DuelHint"),
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
        });

        _cards.Children.Add(_left);
        _cards.Children.Add(_right);
        Layout(stacked: false);
        Children.Add(_cards);
        Children.Add(_empty);
        Children.Add(_skip);

        _left.Picked += card => Pick(card);
        _right.Picked += card => Pick(card);
        _skip.Click += (_, _) => Skip();
        // Tunnelling, so a focused card button never sees Space (its own click)
        // and the arrows do not move focus instead of picking. Enter is left to
        // the button: it picks the focused card.
        PreviewKeyDown += Panel_KeyDown;
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
        Loaded += (_, _) => Refresh();
    }

    /// <summary>Shows the current pair again, replacing it if it became invalid (hidden, queued, gone).</summary>
    public void Refresh()
    {
        if (_home is null) return;
        var (current, upcoming) = _home.DuelPairs();
        _count.Text = _home.DuelPicked > 0 ? UiText.Format("Home_DuelPicked", _home.DuelPicked) : "";
        _count.Visibility = _home.DuelPicked > 0 ? Visibility.Visible : Visibility.Collapsed;

        var any = current is not null;
        _cards.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _skip.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        if (current is not { } pair) return;

        _left.Show(pair.Left, _home.Describe(pair.Left), _pictures);
        _right.Show(pair.Right, _home.Describe(pair.Right), _pictures);
        // Decode the next pair now, so the swap after a pick is immediate.
        var keep = new List<CardBase> { pair.Left, pair.Right };
        if (upcoming is { } next)
        {
            _pictures.Preload(next.Left);
            _pictures.Preload(next.Right);
            keep.Add(next.Left);
            keep.Add(next.Right);
        }
        _pictures.KeepOnly(keep);
    }

    private void Pick(CardBase card)
    {
        if (_home is null) return;
        // Move on first, save after: the next pair must not wait for the disk.
        _home.DuelPick();
        Refresh();
        CardStateActions.ToggleAsync([card.FilePath], CardFlag.Queued)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Home.DuelPick");
    }

    private void Skip()
    {
        if (_home is null) return;
        _home.DuelSkip();
        Refresh();
    }

    // Left / Right pick, Down or Space skip — while focus is in the panel and
    // not in a text box (there is none here today; the guard keeps it true).
    private void Panel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox) return;
        switch (e.Key)
        {
            case VirtualKey.Left when _left.Card is { } left:
                Pick(left);
                break;
            case VirtualKey.Right when _right.Card is { } right:
                Pick(right);
                break;
            case VirtualKey.Down or VirtualKey.Space when _skip.Visibility == Visibility.Visible:
                Skip();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Fit(double width)
    {
        var stacked = width < StackBelow;
        if (stacked != _stacked) Layout(stacked);
        var available = Math.Min(width, MaxWidthBoth);
        var cardWidth = stacked ? available : (available - _cards.ColumnSpacing) / 2;
        _left.SetPictureWidth(cardWidth);
        _right.SetPictureWidth(cardWidth);
    }

    private void Layout(bool stacked)
    {
        _stacked = stacked;
        _cards.ColumnDefinitions.Clear();
        _cards.RowDefinitions.Clear();
        _cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (stacked)
        {
            _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _cards.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(_right, 0);
            Grid.SetRow(_right, 1);
        }
        else
        {
            _cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(_right, 1);
            Grid.SetRow(_right, 0);
        }
    }

    /// <summary>One of the two: a large 16:9 picture and the caption under it.</summary>
    private sealed class DuelCardView : StackPanel
    {
        private readonly Button _open = new()
        {
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Colors.Transparent),
            CornerRadius = new CornerRadius(8),
        };
        private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
        private readonly Border _frame;
        private readonly HomeCardCaption _caption = new(large: true);
        private int _generation;

        public CardBase? Card { get; private set; }

        public event Action<CardBase>? Picked;

        public DuelCardView(string automationId)
        {
            Spacing = 6;
            AutomationProperties.SetAutomationId(_open, automationId);
            _frame = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["ControlStrongFillColorDefaultBrush"],
                Child = _image,
                Height = 200,
            };
            _open.Content = _frame;
            // Click, Enter and Space are all the button's own Click.
            _open.Click += (_, _) => { if (Card is { } card) Picked?.Invoke(card); };
            Children.Add(_open);
            Children.Add(_caption);
        }

        public void SetPictureWidth(double width)
        {
            if (width <= 0) return;
            _frame.Height = Math.Round(width * 9 / 16);
        }

        public void Show(CardBase card, HomeCardInfo info, DecodedPictures pictures)
        {
            _caption.Show(info, _open);
            if (ReferenceEquals(Card, card)) return;
            Card = card;
            var generation = ++_generation;
            if (pictures.Ready(card) is { } ready)
            {
                _image.Source = ready;
                return;
            }
            // The gallery's thumbnail fills the frame until the sharp picture has decoded.
            _image.Source = card.ThumbnailUri is { } thumbnail
                ? new BitmapImage { DecodePixelWidth = 400, UriSource = thumbnail }
                : null;
            pictures.GetAsync(card).ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result is { } full && generation == _generation)
                    _image.Source = full;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }

    /// <summary>
    /// The large pictures, decoded once each and kept while they are on screen
    /// or next: at most the current and the upcoming pair.
    /// </summary>
    internal sealed class DecodedPictures(int decodeWidth)
    {
        private readonly Dictionary<string, Task<BitmapImage?>> _pictures = new(StringComparer.OrdinalIgnoreCase);

        public void Preload(CardBase card) => _ = GetAsync(card);

        public BitmapImage? Ready(CardBase card)
            => _pictures.TryGetValue(card.FilePath, out var task) && task.Status == TaskStatus.RanToCompletion
                ? task.Result : null;

        public Task<BitmapImage?> GetAsync(CardBase card)
        {
            if (!_pictures.TryGetValue(card.FilePath, out var task))
                _pictures[card.FilePath] = task = DecodeAsync(card);
            return task;
        }

        public void KeepOnly(IEnumerable<CardBase> cards)
        {
            var keep = new HashSet<string>(cards.Select(c => c.FilePath), StringComparer.OrdinalIgnoreCase);
            foreach (var path in _pictures.Keys.Where(p => !keep.Contains(p)).ToList()) _pictures.Remove(path);
        }

        // Created on the UI thread (a BitmapImage must be); SetSourceAsync
        // decodes off it, at no more than the width it is shown at.
        private async Task<BitmapImage?> DecodeAsync(CardBase card)
        {
            try
            {
                var picture = new BitmapImage { DecodePixelWidth = Math.Min(decodeWidth, card.Width > 0 ? card.Width : decodeWidth) };
                var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
                using var stream = await file.OpenReadAsync();
                await picture.SetSourceAsync(stream);
                return picture;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // The thumbnail stays; the card is still pickable.
                App.Services.GetRequiredService<IAppLogger>().LogError("Home.DuelPicture", ex, card.FilePath);
                return null;
            }
        }
    }
}

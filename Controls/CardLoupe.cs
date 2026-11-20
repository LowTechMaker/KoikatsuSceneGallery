using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// A card looked at large, in place of the grid it was opened from (wave 14).
/// The details pane stays beside it, Left and Right step through the cards the
/// grid shows, and Esc, the close button or a double click on the picture goes
/// back to the grid with the card still selected.
/// </summary>
/// <remarks>
/// It replaces "open = go to another page" (a phone album's full-screen photo
/// with a back button) for the library. The pieces are the detail page's:
/// <see cref="ProgressivePreview"/> for a sharp picture without a blank frame,
/// <see cref="Filmstrip"/> for the neighbours, and the detail page's drag-out.
/// </remarks>
internal sealed class CardLoupe : Grid
{
    private const int DecodeWidth = 2560;

    private readonly Image _image = new() { Stretch = Stretch.Uniform, CanDrag = true };
    private readonly ScrollViewer _scroll = new()
    {
        ZoomMode = ZoomMode.Enabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollMode = ScrollMode.Auto,
        VerticalScrollMode = ScrollMode.Auto,
        MinZoomFactor = 0.25f,
        MaxZoomFactor = 8,
    };
    private readonly ProgressivePreview _preview;
    private readonly Filmstrip _filmstrip;
    private readonly TextBlock _title = new()
    {
        Style = (Style)Application.Current.Resources["SectionTitleTextBlockStyle"],
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock _position = new()
    {
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button _prev = IconButton("", "Loupe_Previous", "LoupePrevious");
    private readonly Button _next = IconButton("", "Loupe_Next", "LoupeNext");
    private readonly ToggleButton _actual = new() { Content = "100%", Padding = new Thickness(8, 4, 8, 4) };
    private readonly Button _close = IconButton("", "Loupe_Close", "LoupeClose");

    private IReadOnlyList<CardBase> _cards = [];
    private int _index = -1;

    /// <summary>The card on show changed (stepping, or the filmstrip); the host moves its selection.</summary>
    public event Action<CardBase>? CurrentChanged;

    /// <summary>The user left the loupe; the host shows the grid again with this card selected.</summary>
    public event Action<CardBase?>? Closed;

    /// <summary>The card on show, or null when the loupe is closed.</summary>
    public CardBase? Current => _index >= 0 && _index < _cards.Count ? _cards[_index] : null;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CardLoupe()
    {
        _preview = new ProgressivePreview(_image);
        _filmstrip = new Filmstrip(card => ShowAt(IndexOf(card)));
        Visibility = Visibility.Collapsed;
        Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        AutomationProperties.SetAutomationId(this, "CardLoupe");
        AutomationProperties.SetName(this, UiText.Get("Loupe_Name"));
        AutomationProperties.SetAutomationId(_image, "LoupeImage");
        AutomationProperties.SetAutomationId(_actual, "LoupeActualSize");
        ToolTipService.SetToolTip(_actual, UiText.Get("Loupe_ActualSize"));
        IsTabStop = true;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var bar = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 0, 0, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        heading.Children.Add(_title);
        heading.Children.Add(_position);
        bar.Children.Add(heading);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        buttons.Children.Add(_prev);
        buttons.Children.Add(_next);
        buttons.Children.Add(_actual);
        buttons.Children.Add(_close);
        SetColumn(buttons, 1);
        bar.Children.Add(buttons);
        Children.Add(bar);

        _scroll.Content = _image;
        SetRow(_scroll, 1);
        Children.Add(_scroll);
        SetRow(_filmstrip, 2);
        Children.Add(_filmstrip);

        _prev.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
        _close.Click += (_, _) => Close();
        _actual.Click += (_, _) => ApplyZoom();
        _image.DoubleTapped += (_, e) => { e.Handled = true; Close(); };
        _image.DragStarting += (_, e) => DetailNavigationHelper.HandleDragStartingAsync(Current, e)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Loupe.PrepareDrag");
        _scroll.SizeChanged += (_, _) => ApplyZoom();
        KeyDown += Loupe_KeyDown;
    }

    /// <summary>Shows <paramref name="cards"/>[<paramref name="index"/>] over the grid.</summary>
    public void Open(IReadOnlyList<CardBase> cards, int index)
    {
        if (cards.Count == 0) return;
        _cards = cards;
        Visibility = Visibility.Visible;
        ShowAt(Math.Clamp(index, 0, cards.Count - 1));
        DispatcherQueue.TryEnqueue(() => Focus(FocusState.Programmatic));
    }

    /// <summary>The grid's list changed under the loupe (a refresh); keeps the current card when it is still there.</summary>
    public void UpdateCards(IReadOnlyList<CardBase> cards)
    {
        if (!IsOpen) return;
        var current = Current;
        _cards = cards;
        if (cards.Count == 0) { Close(); return; }
        var index = current is null ? -1 : IndexOf(current);
        ShowAt(index >= 0 ? index : Math.Clamp(_index, 0, cards.Count - 1), notify: index < 0);
    }

    public void Close()
    {
        if (!IsOpen) return;
        var current = Current;
        Visibility = Visibility.Collapsed;
        _image.Source = null;
        Closed?.Invoke(current);
    }

    private void Step(int delta)
    {
        if (_cards.Count == 0) return;
        var target = _index + delta;
        if (target < 0 || target >= _cards.Count) return;
        ShowAt(target);
    }

    private void ShowAt(int index, bool notify = true)
    {
        if (index < 0 || index >= _cards.Count) return;
        _index = index;
        var card = _cards[index];
        _preview.Show(card, card.Width > 0 ? card.Width : DecodeWidth);
        _title.Text = card.FileName;
        _position.Text = $"{index + 1} / {_cards.Count}";
        _prev.IsEnabled = index > 0;
        _next.IsEnabled = index < _cards.Count - 1;
        _filmstrip.Show(_cards, index);
        ApplyZoom();
        if (notify) CurrentChanged?.Invoke(card);
    }

    // Fit: the picture scaled into the view. 100%: one image pixel per screen
    // pixel, scrollable; the zoom factor undoes the window's scale.
    private void ApplyZoom()
    {
        if (Current is not { } card || _scroll.ViewportWidth <= 0 || _scroll.ViewportHeight <= 0) return;
        if (_actual.IsChecked == true && card.Width > 0)
        {
            _image.Stretch = Stretch.None;
            _image.MaxWidth = double.PositiveInfinity;
            _image.MaxHeight = double.PositiveInfinity;
            var scale = XamlRoot?.RasterizationScale ?? 1;
            _scroll.ChangeView(null, null, (float)(1 / scale), true);
        }
        else
        {
            _image.Stretch = Stretch.Uniform;
            _image.MaxWidth = _scroll.ViewportWidth;
            _image.MaxHeight = _scroll.ViewportHeight;
            _scroll.ChangeView(0, 0, 1, true);
        }
    }

    private void Loupe_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Left: Step(-1); break;
            case VirtualKey.Right: Step(1); break;
            case VirtualKey.Home: ShowAt(0); break;
            case VirtualKey.End: ShowAt(_cards.Count - 1); break;
            case VirtualKey.Escape: Close(); break;
            default: return; // Q / F / X and 1-3 go on to the host (LibraryBrowser)
        }
        e.Handled = true;
    }

    private int IndexOf(CardBase card)
    {
        for (var i = 0; i < _cards.Count; i++)
            if (string.Equals(_cards[i].FilePath, card.FilePath, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    private static Button IconButton(string glyph, string tooltipKey, string automationId)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Padding = new Thickness(8, 6, 8, 6),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
        };
        var tip = UiText.Get(tooltipKey);
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, tip);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}

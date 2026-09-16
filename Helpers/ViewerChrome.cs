using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Shared viewer controls while each page retains its metadata and version handling.</summary>
internal sealed class ViewerChrome
{
    private readonly Page _page;
    private readonly Image _image;
    private readonly Func<CardBase?> _current;
    private readonly Action<CardBase> _show;
    private readonly Button _previous, _next;
    private readonly ScrollViewer _scroll, _inspector;
    private readonly Grid _body, _header;
    private readonly StackPanel _actions;
    private readonly TextBlock _position = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _breadcrumb = new() { TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0,0,0,8) };
    private readonly ToggleButton _info = new();
    private bool _fit = true, _wide = true, _refreshQueued;
    public BrowseContext? Context { get; private set; }
    public ViewerChrome(Page page, Image image, Func<CardBase?> current, Action<CardBase> show)
    {
        _page = page; _image = image; _current = current; _show = show;
        _previous = (Button)page.FindName("PrevButton"); _next = (Button)page.FindName("NextButton");
        var root = (Grid)page.Content;
        _body = root.Children.OfType<Grid>().Single(g => Grid.GetRow(g) == 1);
        _scroll = _body.Children.OfType<ScrollViewer>().First(s => Grid.GetColumn(s) == 0);
        _inspector = _body.Children.OfType<ScrollViewer>().First(s => s != _scroll);
        var header = _header = root.Children.OfType<Grid>().Single(g => Grid.GetRow(g) == 0);
        var actions = _actions = header.Children.OfType<StackPanel>().Single();
        actions.Children.Insert(0, _position);
        var fit = new Button { Content = new FontIcon { Glyph = "\uE9A6", FontSize = 14 } };
        ToolTipService.SetToolTip(fit, UiText.Get("Viewer_Fit")); AutomationProperties.SetName(fit, UiText.Get("Viewer_Fit"));
        fit.Click += (_, _) => { _fit = true; Fit(); }; actions.Children.Add(fit);
        var original = new Button { Content = "100%" };
        ToolTipService.SetToolTip(original, UiText.Get("Viewer_Original")); AutomationProperties.SetName(original, UiText.Get("Viewer_Original"));
        original.Click += (_, _) => Original(); actions.Children.Add(original);
        _info.Content = new FontIcon { Glyph = "\uE946", FontSize = 14 }; _info.IsChecked = true;
        ToolTipService.SetToolTip(_info, UiText.Get("Viewer_Info")); AutomationProperties.SetName(_info, UiText.Get("Viewer_Info"));
        _info.Click += (_, _) => Layout(); actions.Children.Add(_info);
        header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        header.RowDefinitions.Add(new() { Height = GridLength.Auto }); header.RowDefinitions.Add(new() { Height = GridLength.Auto });
        foreach (var child in header.Children) Grid.SetRow((FrameworkElement)child, 1);
        Grid.SetColumnSpan(_breadcrumb, 3); header.Children.Add(_breadcrumb);
        foreach (var text in header.Children.OfType<TextBlock>().Where(t => t != _breadcrumb)) { text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxLines = 1; }
        _body.SizeChanged += (_, _) => { bool wide = _body.ActualWidth >= 900; if (wide != _wide) { _wide = wide; _info.IsChecked = wide; } Layout(); };
        _scroll.SizeChanged += (_, _) => { if (_fit) Fit(); };
        var libraries = App.Services.GetRequiredService<KoikatsuSceneGallery.Services.LibraryRegistry>().All;
        page.Loaded += (_, _) => { foreach (var library in libraries) library.CardsChanged += CardsChanged; };
        page.Unloaded += (_, _) => { foreach (var library in libraries) library.CardsChanged -= CardsChanged; };
        page.Loaded += (_, _) => { _wide = _body.ActualWidth >= 900; _info.IsChecked = _wide; Layout(); Fit(); };
    }
    public void SetContext(BrowseContext context) { Context = context; _breadcrumb.Text = context.Title; }
    /// <summary>
    /// Position to show when the card on screen is not in the browse list, as
    /// a one-based index and a total. Null to show nothing.
    /// </summary>
    /// <remarks>
    /// A character occupies one gallery tile however many cards it has, so
    /// opening a sibling version from the detail page lands on a card the list
    /// the user came from does not contain. That used to print "0 / 2".
    /// </remarks>
    public Func<(int Index, int Total)?>? FallbackPosition { get; set; }

    public bool Update()
    {
        if (Context is null) return false;
        var cards = Context.CurrentCards(); var index = Index(cards);
        if (index < 0)
        {
            // Off the list: report where the page says we are, and hand
            // navigation back to it — walking this list cannot get us out.
            var fallback = FallbackPosition?.Invoke();
            _position.Text = fallback is { Index: > 0 } position
                ? UiText.Format("Viewer_Position", position.Index, position.Total)
                : "";
            if (_current() is { } offList) Context.ReturnPath = offList.FilePath;
            _page.DispatcherQueue.TryEnqueue(() => { if (_fit) Fit(); });
            return false;
        }

        _position.Text = UiText.Format("Viewer_Position", index + 1, cards.Count);
        _previous.IsEnabled = index > 0; _next.IsEnabled = index >= 0 && index < cards.Count - 1;
        if (_current() is { } card) Context.ReturnPath = card.FilePath;
        _page.DispatcherQueue.TryEnqueue(() => { if (_fit) Fit(); });
        return true;
    }
    private int Index(IReadOnlyList<CardBase> cards)
    {
        for (int i = 0; i < cards.Count; i++) if (cards[i].FilePath.Equals(_current()?.FilePath, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }
    public bool Navigate(int direction, bool random = false)
    {
        if (Context is null) return false;
        var cards = Context.CurrentCards(); var index = Index(cards);
        // Same reason as in Update: the page owns navigation for a card that is
        // not in this list.
        if (index < 0 && !random) return false;
        var next = index + direction;
        if (random && cards.Count > 0)
        { next = cards.Count == 1 ? 0 : (Math.Max(0, index) + 1 + Random.Shared.Next(cards.Count - 1)) % cards.Count; }
        if (next >= 0 && next < cards.Count)
        {
            _fit = true;
            if (cards[next].GetType() == _current()?.GetType()) _show(cards[next]);
            else Context.Open(_page.Frame, cards[next], true);
        }
        return true;
    }
    private void CardsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_refreshQueued) return;
        _refreshQueued = true;
        _page.DispatcherQueue.TryEnqueue(() => { _refreshQueued = false; RecoverIfMissing(); Update(); });
    }
    public bool RecoverIfMissing()
    {
        if (!ReferenceEquals(_page.Frame?.Content, _page) || Context is null || _current() is not { } current) return false;
        var live = Context.CurrentCards();
        if (live.Any(c => c.FilePath.Equals(current.FilePath, StringComparison.OrdinalIgnoreCase))) return false;
        var next = BrowseSequence.AfterRemoval(Context.Cards, live, current.FilePath, c => c.FilePath);
        if (next is null) { if (_page.Frame.CanGoBack) _page.Frame.GoBack(); }
        else if (next.GetType() == current.GetType()) _show(next);
        else Context.Open(_page.Frame, next, true);
        return true;
    }
    public static bool IsEditing(XamlRoot root) => FocusManager.GetFocusedElement(root) is TextBox or PasswordBox or RichEditBox;
    private void Layout()
    {
        bool visible = _info.IsChecked == true;
        Grid.SetRow(_actions, _wide ? 1 : 2); Grid.SetColumn(_actions, _wide ? 2 : 0);
        Grid.SetColumnSpan(_actions, _wide ? 1 : 3);
        _actions.HorizontalAlignment = _wide ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _actions.Margin = _wide ? new Thickness(0) : new Thickness(0, 8, 0, 0);
        _body.ColumnDefinitions[1].Width = new(_wide && visible ? 320 : 0);
        Grid.SetColumn(_inspector, _wide ? 1 : 0);
        _inspector.Width = Math.Min(320, Math.Max(0, _body.ActualWidth));
        _inspector.HorizontalAlignment = HorizontalAlignment.Right;
        _inspector.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _inspector.Background = _wide ? null : (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        _body.ColumnSpacing = _wide && visible ? 24 : 0;
    }
    private void Fit()
    {
        if (_scroll.ActualWidth <= 0 || _scroll.ActualHeight <= 0) return;
        _image.Width = double.NaN; _image.Height = double.NaN;
        _image.MaxWidth = _scroll.ActualWidth; _image.MaxHeight = _scroll.ActualHeight;
        _image.Stretch = Stretch.Uniform; _scroll.ChangeView(0, 0, 1, true);
    }
    private void Original()
    {
        if (_current() is not { } card) return;
        _fit = false; _image.MaxWidth = double.PositiveInfinity; _image.MaxHeight = double.PositiveInfinity;
        var scale = _page.XamlRoot.RasterizationScale;
        _image.Width = card.Width / scale; _image.Height = card.Height / scale;
        _image.Source = new BitmapImage(card.FileUri);
        _scroll.ChangeView(0, 0, 1, true);
    }
}

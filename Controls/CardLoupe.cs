using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Composition;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// A card looked at large, in place of the grid it was opened from: the
/// library, the home page, and the viewer page all use it, with the details
/// pane beside it.
/// </summary>
/// <remarks>
/// <para>The layout, from the top:</para>
/// <list type="bullet">
/// <item>a header: what the card is (post title, character or outfit name)
/// over its file name and place in the list, then the zoom controls, the
/// details pane toggle and close;</item>
/// <item>the stage, darker than the page so the picture stands out, with large
/// previous / next buttons on its edges that show while the pointer is over it,
/// and a line of key hints that fades out a few seconds after opening;</item>
/// <item>the filmstrip of neighbouring cards.</item>
/// </list>
/// <para>
/// Zoom works like a desktop photo viewer: the wheel zooms around the pointer,
/// a drag pans once zoomed, a double click toggles between fit and close-up,
/// and + / - / 0 zoom in, out and back to fit. At fit, dragging the picture
/// still drags the card file out (to the game), as from a tile.
/// </para>
/// <para>
/// The first version (wave 14) put small arrows in a corner, closed on a
/// double click and had only Fit / 100%; the user asked for a proper design.
/// The second (wave 16) was still "ugly" (user, with a screenshot): a 320x180
/// scene card stretched four times over was a blur, the library's toolbar
/// stayed above it, the title repeated the details pane's, and a dark box
/// around the picture read as letterboxing. So: the picture is never shown
/// at more than twice its own pixels, the header is only back, position and
/// zoom (the pane says what the card is), there is no box, and the host lets
/// the loupe cover its whole content area.
/// </para>
/// </remarks>
internal sealed class CardLoupe : Grid
{
    private const int DecodeWidth = 2560;
    private const float MaxZoom = 8;
    private const double WheelStep = 1.2;
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan HintDuration = TimeSpan.FromSeconds(4);

    private readonly SettingsViewModel _settings = App.Services.GetRequiredService<SettingsViewModel>();

    // ---- header
    private readonly Grid _bar = new() { ColumnSpacing = 12, Padding = new Thickness(0, 0, 0, 10) };
    private readonly Button _back = new()
    {
        Padding = new Thickness(10, 6, 12, 6),
        Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
    };
    private readonly TextBlock _backLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _position = new()
    {
        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button _zoomOut = IconButton("", "Loupe_ZoomOut", "LoupeZoomOut");
    private readonly Button _zoomIn = IconButton("", "Loupe_ZoomIn", "LoupeZoomIn");
    private readonly Button _zoomLevel = new()
    {
        MinWidth = 64,
        Padding = new Thickness(8, 5, 8, 5),
        Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
    };
    private readonly Button _info = IconButton("", "Inspector_Toggle", "LoupeToggleInfo");

    // ---- stage
    private readonly Border _stage = new();
    private readonly Grid _stageContent = new();
    private readonly ScrollViewer _scroll = new()
    {
        ZoomMode = ZoomMode.Enabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
        HorizontalScrollMode = ScrollMode.Enabled,
        VerticalScrollMode = ScrollMode.Enabled,
        MinZoomFactor = 1,
        MaxZoomFactor = MaxZoom,
        IsTabStop = true,
    };
    // The scroll viewer's content: the size of the stage, the picture centred in it.
    private readonly Grid _canvas = new();
    private readonly Image _image = new()
    {
        Stretch = Stretch.Uniform,
        CanDrag = true,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button _prev = EdgeButton("", "Loupe_Previous", "LoupePrevious", HorizontalAlignment.Left);
    private readonly Button _next = EdgeButton("", "Loupe_Next", "LoupeNext", HorizontalAlignment.Right);
    private readonly Border _hint = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 12),
        Padding = new Thickness(12, 6, 12, 6),
        IsHitTestVisible = false,
    };
    private readonly DispatcherTimer _hintTimer = new() { Interval = HintDuration };

    private readonly ProgressivePreview _preview;
    private readonly Filmstrip _filmstrip;

    private IReadOnlyList<CardBase> _cards = [];
    private int _index = -1;
    private bool _closing;
    private bool _pointerOverStage;
    // Dragging to pan, once zoomed in.
    private uint? _panPointer;
    private Point _panFrom;
    private double _panOffsetX, _panOffsetY;

    /// <summary>The card on show changed (stepping, or the filmstrip); the host moves its selection.</summary>
    public event Action<CardBase>? CurrentChanged;

    /// <summary>The user left the loupe; the host shows the grid again with this card selected.</summary>
    public event Action<CardBase?>? Closed;

    /// <summary>
    /// Where a card's tile is, in window coordinates (TransformToVisual(null)),
    /// for the zoom in and out; null when it is not on screen (then the loupe
    /// just appears). Set by the host, which scrolls the tile into view first.
    /// Window coordinates, not the loupe's own: the host moves the loupe when
    /// it opens and closes (it hides and shows its toolbar), and a rectangle
    /// relative to the loupe went stale with it (user report: the picture flew
    /// from a place above the tile).
    /// </summary>
    public Func<CardBase, Rect?>? TileBounds { get; set; }

    /// <summary>
    /// Raised as the loupe starts to close, before the tile is measured for the
    /// flight back: the host puts its own layout back first (its toolbar), so
    /// the tile is measured where it will be once the loupe is gone.
    /// </summary>
    public event Action? Closing;

    /// <summary>The card on show, or null when the loupe is closed.</summary>
    public CardBase? Current => _index >= 0 && _index < _cards.Count ? _cards[_index] : null;

    public bool IsOpen => Visibility == Visibility.Visible;

    public CardLoupe()
    {
        _preview = new ProgressivePreview(_image);
        _filmstrip = new Filmstrip(card => ShowAt(IndexOf(card)));
        Visibility = Visibility.Collapsed;
        AutomationProperties.SetAutomationId(this, "CardLoupe");
        AutomationProperties.SetName(this, UiText.Get("Loupe_Name"));
        AutomationProperties.SetAutomationId(_image, "LoupeImage");
        AutomationProperties.SetAutomationId(_scroll, "LoupeScroll");
        AutomationProperties.SetAutomationId(_zoomLevel, "LoupeZoomLevel");
        ToolTipService.SetToolTip(_zoomLevel, UiText.Get("Loupe_ZoomLevelTip"));

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // The page behind does not show through while the loupe is up.
        Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];

        BuildHeader();
        BuildStage();
        SetRow(_filmstrip, 2);
        Children.Add(_filmstrip);

        _hintTimer.Tick += (_, _) => { _hintTimer.Stop(); FadeHint(show: false); };
    }

    // ------------------------------------------------------------ layout

    private void BuildHeader()
    {
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Back, and where in the list this card is. What the card is, the pane says.
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _backLabel.Text = UiText.Get("Loupe_BackLabel");
        _back.Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { new FontIcon { Glyph = "\uE72B", FontSize = 14 }, _backLabel },
        };
        var backTip = UiText.Get("Loupe_Close");
        ToolTipService.SetToolTip(_back, backTip);
        AutomationProperties.SetName(_back, backTip);
        AutomationProperties.SetAutomationId(_back, "LoupeClose");
        heading.Children.Add(_back);
        AutomationProperties.SetAutomationId(_position, "LoupePosition");
        heading.Children.Add(_position);
        _bar.Children.Add(heading);

        // Zoom: out, the level (click: back to fit, or to the close-up), in.
        var zoom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(2),
            CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
        };
        zoom.Children.Add(_zoomOut);
        zoom.Children.Add(_zoomLevel);
        zoom.Children.Add(_zoomIn);
        SetColumn(zoom, 1);
        _bar.Children.Add(zoom);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_info);
        SetColumn(actions, 2);
        _bar.Children.Add(actions);
        Children.Add(_bar);

        _zoomOut.Click += (_, _) => ZoomBy(1 / WheelStep, null);
        _zoomIn.Click += (_, _) => ZoomBy(WheelStep, null);
        _zoomLevel.Click += (_, _) => ToggleCloseUp(null);
        _info.Click += (_, _) => _settings.InspectorVisible = !_settings.InspectorVisible;
        _back.Click += (_, _) => Close();
    }

    private void BuildStage()
    {
        // No box of its own: a darker frame read as letterboxing (user report).
        _stage.Child = _stageContent;
        SetRow(_stage, 1);
        Children.Add(_stage);

        _canvas.Children.Add(_image);
        _scroll.Content = _canvas;
        // On from the start: ResetVisual stops a "Translation" animation, which
        // throws when translation was never enabled (a loupe closed without
        // having zoomed in from its tile).
        ElementCompositionPreview.SetIsTranslationEnabled(_scroll, true);
        _stageContent.Children.Add(_scroll);
        _stageContent.Children.Add(_prev);
        _stageContent.Children.Add(_next);

        _hint.CornerRadius = new CornerRadius(14);
        _hint.Background = (Brush)Application.Current.Resources["MediaOverlayBadgeBackgroundBrush"];
        _hint.Child = new TextBlock
        {
            Text = UiText.Get("Loupe_Hint"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Foreground = (Brush)Application.Current.Resources["MediaOverlayForegroundBrush"],
        };
        _hint.Opacity = 0;
        _stageContent.Children.Add(_hint);

        _prev.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
        SetEdgeButtons(visible: false);

        _stageContent.PointerEntered += (_, _) => { _pointerOverStage = true; SetEdgeButtons(visible: true); };
        _stageContent.PointerExited += (_, _) => { _pointerOverStage = false; SetEdgeButtons(visible: false); };

        _scroll.SizeChanged += (_, _) => Fit();
        _scroll.ViewChanged += (_, _) => UpdateZoomState();
        // handledEventsToo: the scroll viewer marks the wheel handled as it scrolls.
        _scroll.AddHandler(PointerWheelChangedEvent, new PointerEventHandler(Scroll_Wheel), handledEventsToo: true);
        _canvas.PointerPressed += Canvas_PointerPressed;
        _canvas.PointerMoved += Canvas_PointerMoved;
        _canvas.PointerReleased += Canvas_PointerReleased;
        _canvas.PointerCaptureLost += (_, _) => _panPointer = null;
        _canvas.DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            ToggleCloseUp(e.GetPosition(_scroll));
        };
        _image.DragStarting += (_, e) => DetailNavigationHelper.HandleDragStartingAsync(Current, e)
            .Observe(App.Services.GetRequiredService<IAppLogger>(), "Loupe.PrepareDrag");
    }

    /// <summary>Changes what the back button says (the viewer page goes back to a page, not to a grid).</summary>
    public void SetCloseTooltip(string text)
    {
        ToolTipService.SetToolTip(_back, text);
        AutomationProperties.SetName(_back, text);
    }

    // ------------------------------------------------------------ open, step, close

    /// <summary>Shows <paramref name="cards"/>[<paramref name="index"/>] over the grid.</summary>
    public void Open(IReadOnlyList<CardBase> cards, int index)
    {
        if (cards.Count == 0) return;
        _cards = cards;
        _closing = false;
        index = Math.Clamp(index, 0, cards.Count - 1);
        // Measured before the loupe covers the grid.
        var from = CardMotion.AnimationsEnabled ? TileBounds?.Invoke(cards[index]) : null;
        Visibility = Visibility.Visible;
        ShowAt(index);
        if (from is { } rect)
        {
            // Hidden until the first layout pass, then flown out of the tile.
            Opacity = 0;
            void Start(object? sender, object e)
            {
                LayoutUpdated -= Start;
                Opacity = 1;
                Zoom(rect, toTile: false, OpenDuration, null);
            }
            LayoutUpdated += Start;
        }
        FadeHint(show: true);
        _hintTimer.Stop();
        _hintTimer.Start();
        // Focus inside the loupe, not left on the grid behind it: there the
        // arrows moved the grid's hidden selection instead of the card on show.
        DispatcherQueue.TryEnqueue(() => _scroll.Focus(FocusState.Programmatic));
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

    /// <summary>
    /// Shows <paramref name="card"/> in the current card's place: a character's
    /// other version chosen in the details pane. Stepping on continues from there.
    /// </summary>
    public void Replace(CardBase card)
    {
        if (!IsOpen || _index < 0) return;
        var index = IndexOf(card);
        if (index >= 0)
        {
            ShowAt(index);
            return;
        }
        var cards = _cards.ToArray();
        cards[_index] = card;
        _cards = cards;
        ShowAt(_index);
    }

    public void Close() => Close(animate: true);

    /// <summary>Closes; animated back into the card's tile when it can be found.</summary>
    public void Close(bool animate)
    {
        if (!IsOpen || _closing) return;
        var current = Current;
        _hintTimer.Stop();
        if (_scroll.ZoomFactor > 1.01f) Fit(); // the fly-back starts from the fitted picture
        Closing?.Invoke();
        UpdateLayout();
        Rect? to = animate && CardMotion.AnimationsEnabled && current is not null ? TileBounds?.Invoke(current) : null;
        if (to is not { } rect)
        {
            Finish();
            return;
        }
        _closing = true;
        Zoom(rect, toTile: true, CloseDuration, Finish);

        void Finish()
        {
            _closing = false;
            Visibility = Visibility.Collapsed;
            try
            {
                ResetVisual();
                _image.Source = null;
            }
            finally
            {
                // Whatever the visual reset does, the host must hear it closed.
                Closed?.Invoke(current);
            }
        }
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
        _preview.Show(card, card.Width > 0 ? Math.Min(card.Width, DecodeWidth) : DecodeWidth);
        _position.Text = $"{index + 1:N0} / {_cards.Count:N0}";
        ToolTipService.SetToolTip(_position, card.FileName);
        _filmstrip.Show(_cards, index);
        SetEdgeButtons(_pointerOverStage);
        Fit();
        if (notify) CurrentChanged?.Invoke(card);
    }

    // ------------------------------------------------------------ zoom and pan

    /// <summary>The most a picture is enlarged to fit: beyond twice its own pixels it is only blur.</summary>
    private const double MaxFitScale = 2;

    /// <summary>
    /// The picture's size when it fits the stage, at most twice its own pixels:
    /// a scene card is 320x180, and filling a large stage with it was a blur.
    /// </summary>
    private Size FitSize()
    {
        double viewW = _scroll.ActualWidth, viewH = _scroll.ActualHeight;
        if (Current is not { } card || viewW <= 0 || viewH <= 0) return new Size(0, 0);
        var aspect = card.Width > 0 && card.Height > 0 ? (double)card.Width / card.Height : 16.0 / 9;
        var width = Math.Min(viewW, viewH * aspect);
        if (card.Width > 0)
            width = Math.Min(width, card.Width * MaxFitScale / (XamlRoot?.RasterizationScale ?? 1));
        return new Size(width, width / aspect);
    }

    /// <summary>The whole picture in view, centred.</summary>
    private void Fit()
    {
        var fit = FitSize();
        if (fit.Width <= 0) return;
        _canvas.Width = _scroll.ActualWidth;
        _canvas.Height = _scroll.ActualHeight;
        _image.Width = fit.Width;
        _image.Height = fit.Height;
        _scroll.ChangeView(0, 0, 1, disableAnimation: true);
        UpdateZoomState();
    }

    /// <summary>How many screen pixels one picture pixel takes now (1 = actual size).</summary>
    private double PixelScale()
    {
        var fit = FitSize();
        if (Current is not { Width: > 0 } card || fit.Width <= 0) return 1;
        return fit.Width * _scroll.ZoomFactor * (XamlRoot?.RasterizationScale ?? 1) / card.Width;
    }

    /// <summary>The zoom at which one picture pixel is one screen pixel (at least fit).</summary>
    private float ActualSizeZoom()
    {
        var fit = FitSize();
        if (Current is not { Width: > 0 } card || fit.Width <= 0) return 2;
        var scale = XamlRoot?.RasterizationScale ?? 1;
        return (float)Math.Clamp(card.Width / (fit.Width * scale), 1, MaxZoom);
    }

    /// <summary>
    /// Zooms by <paramref name="factor"/> keeping the point under
    /// <paramref name="anchor"/> (in the stage's coordinates; null: the centre) still.
    /// </summary>
    private void ZoomBy(double factor, Point? anchor) => ZoomTo(_scroll.ZoomFactor * factor, anchor);

    private void ZoomTo(double target, Point? anchor)
    {
        var from = _scroll.ZoomFactor;
        var to = (float)Math.Clamp(target, 1, MaxZoom);
        if (Math.Abs(to - from) < 0.001) return;
        var point = anchor ?? new Point(_scroll.ActualWidth / 2, _scroll.ActualHeight / 2);
        // The content point under the anchor stays under it.
        var x = (_scroll.HorizontalOffset + point.X) / from * to - point.X;
        var y = (_scroll.VerticalOffset + point.Y) / from * to - point.Y;
        _scroll.ChangeView(Math.Max(0, x), Math.Max(0, y), to, disableAnimation: true);
    }

    /// <summary>Fit when zoomed in; otherwise the actual-size close-up (at least 2x) around the point.</summary>
    private void ToggleCloseUp(Point? anchor)
    {
        if (_scroll.ZoomFactor > 1.01f) Fit();
        else ZoomTo(Math.Max(2, ActualSizeZoom()), anchor);
    }

    private void UpdateZoomState()
    {
        var zoomed = _scroll.ZoomFactor > 1.01f;
        // Fit: dragging the picture takes the card file out (to the game).
        // Zoomed in: dragging pans.
        _image.CanDrag = !zoomed;
        _zoomLevel.Content = $"{Math.Round(PixelScale() * 100)}%";
        _zoomOut.IsEnabled = zoomed;
        _zoomIn.IsEnabled = _scroll.ZoomFactor < MaxZoom - 0.01f;
        ProtectedCursor = InputSystemCursor.Create(zoomed ? InputSystemCursorShape.SizeAll : InputSystemCursorShape.Arrow);
    }

    private void Scroll_Wheel(object sender, PointerRoutedEventArgs e)
    {
        var delta = e.GetCurrentPoint(_scroll).Properties.MouseWheelDelta;
        if (delta == 0) return;
        e.Handled = true;
        ZoomBy(Math.Pow(WheelStep, delta / 120.0), e.GetCurrentPoint(_scroll).Position);
    }

    private void Canvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_scroll.ZoomFactor <= 1.01f) return;
        var point = e.GetCurrentPoint(_scroll);
        if (!point.Properties.IsLeftButtonPressed) return;
        _panPointer = e.Pointer.PointerId;
        _panFrom = point.Position;
        _panOffsetX = _scroll.HorizontalOffset;
        _panOffsetY = _scroll.VerticalOffset;
        _canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Canvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_panPointer != e.Pointer.PointerId) return;
        var at = e.GetCurrentPoint(_scroll).Position;
        _scroll.ChangeView(_panOffsetX - (at.X - _panFrom.X), _panOffsetY - (at.Y - _panFrom.Y), null, disableAnimation: true);
        e.Handled = true;
    }

    private void Canvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_panPointer != e.Pointer.PointerId) return;
        _panPointer = null;
        _canvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    // ------------------------------------------------------------ keys

    /// <summary>
    /// The loupe's own keys, whichever element has focus: the host calls this
    /// from a tunnelling handler, ahead of the scroll viewer (which would pan
    /// with the arrows) and of the grid behind. Returns whether it was one.
    /// </summary>
    public bool HandleKey(VirtualKey key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case VirtualKey.Left: Step(-1); break;
            case VirtualKey.Right: Step(1); break;
            case VirtualKey.Home: ShowAt(0); break;
            case VirtualKey.End: ShowAt(_cards.Count - 1); break;
            case VirtualKey.Escape: Close(); break;
            case VirtualKey.Add or (VirtualKey)187: ZoomBy(WheelStep, null); break; // + on the keypad or the main keys
            case VirtualKey.Subtract or (VirtualKey)189: ZoomBy(1 / WheelStep, null); break; // -
            case VirtualKey.Number0 or VirtualKey.NumberPad0: Fit(); break;
            default: return false; // Q / F / X and 1-3 are the host's
        }
        return true;
    }

    // ------------------------------------------------------------ the open and close flight

    /// <summary>
    /// Flies the picture between the tile and its place in the loupe: the
    /// picture's fitted rectangle is scaled and moved onto the tile's (and
    /// back); the header, the stage's backdrop and the filmstrip fade with it.
    /// </summary>
    private void Zoom(Rect tile, bool toTile, TimeSpan duration, Action? done)
    {
        if (Current is null || _scroll.ActualWidth <= 0 || _scroll.ActualHeight <= 0)
        {
            done?.Invoke();
            return;
        }
        var fit = FitSize();
        double fitX = (_scroll.ActualWidth - fit.Width) / 2, fitY = (_scroll.ActualHeight - fit.Height) / 2;
        // Both in window coordinates (see TileBounds).
        var origin = _scroll.TransformToVisual(null).TransformPoint(new Point(0, 0));
        var scale = (float)(tile.Width / fit.Width);
        var dx = (float)(tile.X - origin.X - fitX * scale);
        var dy = (float)(tile.Y - origin.Y - fitY * scale);

        var visual = ElementCompositionPreview.GetElementVisual(_scroll);
        var compositor = visual.Compositor;
        visual.CenterPoint = new System.Numerics.Vector3(0, 0, 0);
        var easing = compositor.CreateCubicBezierEasingFunction(new(0.1f, 0.9f), new(0.2f, 1f));

        var small = new System.Numerics.Vector3(scale, scale, 1);
        var moved = new System.Numerics.Vector3(dx, dy, 0);
        var scaleAnimation = compositor.CreateVector3KeyFrameAnimation();
        scaleAnimation.InsertKeyFrame(0, toTile ? System.Numerics.Vector3.One : small);
        scaleAnimation.InsertKeyFrame(1, toTile ? small : System.Numerics.Vector3.One, easing);
        scaleAnimation.Duration = duration;
        var moveAnimation = compositor.CreateVector3KeyFrameAnimation();
        moveAnimation.InsertKeyFrame(0, toTile ? System.Numerics.Vector3.Zero : moved);
        moveAnimation.InsertKeyFrame(1, toTile ? moved : System.Numerics.Vector3.Zero, easing);
        moveAnimation.Duration = duration;

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, toTile ? 1f : 0f);
        fade.InsertKeyFrame(1, toTile ? 0f : 1f);
        fade.Duration = duration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation("Scale", scaleAnimation);
        visual.StartAnimation("Translation", moveAnimation);
        foreach (var part in FadingParts())
            ElementCompositionPreview.GetElementVisual(part).StartAnimation("Opacity", fade);
        batch.End();
        batch.Completed += (_, _) =>
        {
            if (!toTile) ResetVisual();
            done?.Invoke();
        };
    }

    // The stage's backdrop fades separately from the picture: it is the Border's
    // background, so the picture (inside it) is animated on its own visual.
    private IEnumerable<UIElement> FadingParts() => [_bar, _filmstrip, _prev, _next];

    private void ResetVisual()
    {
        var visual = ElementCompositionPreview.GetElementVisual(_scroll);
        visual.StopAnimation("Scale");
        visual.StopAnimation("Translation");
        visual.Scale = System.Numerics.Vector3.One;
        visual.Properties.InsertVector3("Translation", System.Numerics.Vector3.Zero);
        foreach (var part in FadingParts())
        {
            var partVisual = ElementCompositionPreview.GetElementVisual(part);
            partVisual.StopAnimation("Opacity");
            partVisual.Opacity = 1;
        }
    }

    // ------------------------------------------------------------ overlays

    private void SetEdgeButtons(bool visible)
    {
        _prev.Visibility = visible && _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        _next.Visibility = visible && _index < _cards.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FadeHint(bool show)
    {
        var visual = ElementCompositionPreview.GetElementVisual(_hint);
        if (!CardMotion.AnimationsEnabled)
        {
            _hint.Opacity = show ? 1 : 0;
            return;
        }
        _hint.Opacity = 1;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, show ? 1f : 0f);
        fade.Duration = TimeSpan.FromMilliseconds(show ? 150 : 600);
        visual.StartAnimation("Opacity", fade);
    }

    // ------------------------------------------------------------ pieces

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
            Padding = new Thickness(9, 7, 9, 7),
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
        };
        var tip = UiText.Get(tooltipKey);
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, tip);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }

    /// <summary>A large round previous / next button on an edge of the stage.</summary>
    private static Button EdgeButton(string glyph, string tooltipKey, string automationId, HorizontalAlignment side)
    {
        var button = new Button
        {
            Width = 44,
            Height = 44,
            Padding = new Thickness(0),
            Margin = new Thickness(12),
            HorizontalAlignment = side,
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(0),
            Background = (Brush)Application.Current.Resources["MediaOverlayBadgeBackgroundBrush"],
            Content = new FontIcon
            {
                Glyph = glyph,
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["MediaOverlayForegroundBrush"],
            },
        };
        var tip = UiText.Get(tooltipKey);
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, tip);
        AutomationProperties.SetAutomationId(button, automationId);
        return button;
    }
}

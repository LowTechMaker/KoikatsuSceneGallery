using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// Select, then act: a click selects a tile and the details pane beside the
/// grid shows it; a double click or Enter opens it; Q / F / X and 1-3 act on
/// whatever is selected (wave 13).
/// </summary>
/// <remarks>
/// It used to be a phone album: a tap opened a full page for one card, and
/// everything else about the card was on that page. Now the grid stays on
/// screen and the card is looked at beside it.
/// </remarks>
public sealed partial class LibraryBrowser
{
    /// <summary>Below this width the pane would squeeze the grid to one or two columns; it hides.</summary>
    private const double InspectorMinBrowserWidth = UiMetrics.InspectorMinHostWidth;
    private const double InspectorWidth = UiMetrics.InspectorWidth;

    private CardInspector _inspector = null!;
    private CardLoupe _loupe = null!;
    private bool _restoringSelection;
    private bool _wideEnoughForInspector = true;

    private void InitInspector()
    {
        _inspector = new CardInspector
        {
            Width = InspectorWidth,
            // Top flush with the grid (it sat 4 px lower). The gap on the left
            // is narrow because the grid's own right padding holds its scroll bar.
            Margin = UiMetrics.InspectorMargin(UiMetrics.PaneGapBesideGrid),
            Frame = () => _frame,
        };
        Grid.SetRow(_inspector, 2);
        Grid.SetColumn(_inspector, 1);
        RootLayout.Children.Add(_inspector);
        _inspector.OpenRequested += card => OpenInLoupe(card);
        // A character's other version: in the loupe's place when it is open,
        // otherwise in the pane only (the grid shows one tile per character).
        _inspector.CardRequested += card =>
        {
            if (_loupe.IsOpen) _loupe.Replace(card);
            else _inspector.Show([card]);
        };
        _inspector.CardGone += () =>
        {
            _loupe.Close(animate: false);
            _inspector.Show([]);
        };

        // The loupe covers the browser's whole content column, toolbar and
        // search included (they cluttered the view above the picture); the
        // details pane stays beside it and grows to the same height.
        _loupe = new CardLoupe();
        Grid.SetRow(_loupe, 0);
        Grid.SetRowSpan(_loupe, 3);
        RootLayout.Children.Add(_loupe);
        _loupe.TileBounds = TileBounds;
        _loupe.CurrentChanged += Loupe_CurrentChanged;
        _loupe.Closing += () => SetLoupeLayout(open: false);
        _loupe.Closed += Loupe_Closed;
        // While the loupe is open its keys win wherever focus is (it may still be
        // on the grid behind, or in the details pane); typing in a text box is left alone.
        PreviewKeyDown += (_, e) =>
        {
            if (!_loupe.IsOpen || e.OriginalSource is TextBox or AutoSuggestBox or PasswordBox) return;
            if (CardKeys.IsModifierDown()) return;
            if (_loupe.HandleKey(e.Key)) { e.Handled = true; return; }
            if (e.KeyStatus.WasKeyDown) return;
            if (CardKeys.Flag(e.Key) is { } flag) { e.Handled = true; _inspector.Toggle(flag); }
            else if (CardKeys.Rating(e.Key) is { } rating) { e.Handled = true; _inspector.RateFromKey(rating); }
        };

        var tip = UiText.Get("Inspector_Toggle");
        ToolTipService.SetToolTip(InspectorToggle, tip);
        AutomationProperties.SetName(InspectorToggle, tip);
        _settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_settings.InspectorVisible)) UpdateInspectorVisibility();
        };
        SizeChanged += (_, e) =>
        {
            var wide = e.NewSize.Width >= InspectorMinBrowserWidth;
            if (wide == _wideEnoughForInspector) return;
            _wideEnoughForInspector = wide;
            UpdateInspectorVisibility();
        };
        UpdateInspectorVisibility();
    }

    private void UpdateInspectorVisibility()
    {
        InspectorToggle.IsChecked = _settings.InspectorVisible;
        _inspector.Visibility = _settings.InspectorVisible && _wideEnoughForInspector
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InspectorToggle_Click(object sender, RoutedEventArgs e)
        => _settings.InspectorVisible = InspectorToggle.IsChecked == true;

    private void Inspector_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs e)
    {
        _settings.InspectorVisible = !_settings.InspectorVisible;
        e.Handled = true;
    }

    /// <summary>The cards behind the selected tiles, a group's members included, in grid order.</summary>
    private IReadOnlyList<CardBase> SelectedCards()
    {
        if (GalleryGrid.SelectedItems.Count == 0) return [];
        // A set, not SelectedItems.Contains per tile: Ctrl+A on a large
        // library would otherwise be quadratic on every selection change.
        var selected = new HashSet<object>(GalleryGrid.SelectedItems, ReferenceEqualityComparer.Instance);
        return [.. _items.Where(selected.Contains).SelectMany(entry => entry.Members)];
    }

    private void Grid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var removed in e.RemovedItems.OfType<GalleryEntry>()) removed.IsSelected = false;
        foreach (var added in e.AddedItems.OfType<GalleryEntry>()) added.IsSelected = true;
        if (_restoringSelection) return;
        _inspector.Show(SelectedCards());
    }

    /// <summary>The selected cards' paths, so Refresh can select them again after rebuilding the tiles.</summary>
    private HashSet<string> RememberSelection()
        => new(SelectedCards().Select(card => card.FilePath), StringComparer.OrdinalIgnoreCase);

    /// <summary>Selects again every tile with a remembered card, then shows what is selected now.</summary>
    private void RestoreSelection(HashSet<string> paths)
    {
        _restoringSelection = true;
        try
        {
            GalleryGrid.SelectedItems.Clear();
            if (paths.Count > 0)
            {
                foreach (var entry in _items)
                {
                    if (entry.Members.Any(card => paths.Contains(card.FilePath)))
                        GalleryGrid.SelectedItems.Add(entry);
                }
            }
        }
        finally { _restoringSelection = false; }
        _inspector.Show(SelectedCards());
    }

    /// <summary>Selects one tile (the one holding <paramref name="path"/>) and shows it.</summary>
    private void SelectOnly(GalleryEntry entry)
    {
        _restoringSelection = true;
        try
        {
            GalleryGrid.SelectedItems.Clear();
            GalleryGrid.SelectedItems.Add(entry);
        }
        finally { _restoringSelection = false; }
        _inspector.Show(SelectedCards());
    }

    private void Grid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not GalleryEntry entry) return;
        e.Handled = true;
        OpenEntry(entry);
    }

    // Tunnelling, so the grid's own key handling (Space selects, letters do
    // nothing useful) does not get there first. Plain keys only: Ctrl+A and
    // the other shortcuts are left alone.
    private void Grid_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.KeyStatus.WasKeyDown) return;
        if (e.Key == VirtualKey.Enter)
        {
            if (FocusedEntry() is { } focused)
            {
                e.Handled = true;
                OpenEntry(focused);
            }
            return;
        }
        if (CardKeys.IsModifierDown()) return;
        if (CardKeys.Flag(e.Key) is { } flag)
        {
            e.Handled = true;
            _inspector.Toggle(flag);
        }
        else if (CardKeys.Rating(e.Key) is { } rating)
        {
            e.Handled = true;
            _inspector.RateFromKey(rating);
        }
    }

    private GalleryEntry? FocusedEntry()
    {
        if (XamlRoot is null) return null;
        return FocusManager.GetFocusedElement(XamlRoot) is GridViewItem item
            ? GalleryGrid.ItemFromContainer(item) as GalleryEntry
            : null;
    }

    /// <summary>Every card the grid shows, group members together, in grid order: what the loupe steps through.</summary>
    private IReadOnlyList<CardBase> VisibleCards() => [.. _items.SelectMany(entry => entry.Members)];

    /// <summary>Opens a card large over the grid, in place, instead of going to its page.</summary>
    private void OpenInLoupe(CardBase card)
    {
        var cards = VisibleCards();
        var index = -1;
        for (var i = 0; i < cards.Count; i++)
            if (ReferenceEquals(cards[i], card)) { index = i; break; }
        // A card the grid does not show (a character's other version, from the pane): on its own.
        if (index < 0) { cards = [card]; index = 0; }
        OpeningDetail?.Invoke();
        // The picture is large on the left now; the pane keeps the facts and actions.
        _inspector.ShowPreview = false;
        SetLoupeLayout(open: true);
        _loupe.Open(cards, index);
    }

    /// <summary>
    /// While the loupe is open the pane runs the full height beside it, and the
    /// library's tab heading (drawn by the window over this control) steps aside.
    /// </summary>
    private void SetLoupeLayout(bool open)
    {
        _loupeLayout = open;
        // The toolbar and the search row span both columns, so with the pane
        // running the full height they drew over it (user report, "wtf"): they
        // go while the loupe is up, and so does the room kept for the tabs.
        Header.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        SearchArea.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        // The loupe is a translucent card panel now; the grid behind it goes
        // (not collapsed: that would lose its scroll position and containers).
        GalleryGrid.Opacity = open ? 0 : 1;
        GalleryGrid.IsHitTestVisible = !open;
        EmptyPanel.Opacity = open ? 0 : 1;
        // Beside the loupe panel the pane needs its own gap; beside the grid the
        // grid's scroll-bar gutter is the gap.
        _inspector.Margin = UiMetrics.InspectorMargin(open ? UiMetrics.PaneGap : UiMetrics.PaneGapBesideGrid);
        Grid.SetRow(_inspector, open ? 0 : 2);
        Grid.SetRowSpan(_inspector, open ? 3 : 1);
        if (_scope is null) MainWindow.Instance?.SetLibraryHeadingHidden(open);
        UpdateHeaderLayout(RootLayout.ActualWidth);
    }

    private bool _loupeLayout;

    /// <summary>
    /// Where the card's picture is in the grid, in the loupe's coordinates: what
    /// the loupe zooms out of and back into. The tile is scrolled into view
    /// first (the loupe may have stepped far from where it opened).
    /// </summary>
    private Windows.Foundation.Rect? TileBounds(CardBase card)
    {
        if (EntryOf(card) is not { } entry) return null;
        if (GalleryGrid.ContainerFromItem(entry) is null)
        {
            GalleryGrid.ScrollIntoView(entry);
            GalleryGrid.UpdateLayout();
        }
        if (GalleryGrid.ContainerFromItem(entry) is not FrameworkElement container) return null;
        // The face picture: the largest image in the tile (the stack layers behind a group are smaller).
        Image? picture = null;
        foreach (var image in VisualTreeSearch.FindDescendants<Image>(container))
            if (image.ActualWidth > (picture?.ActualWidth ?? 0)) picture = image;
        FrameworkElement target = picture is { ActualWidth: > 0 } ? picture : container;
        var bounds = target.TransformToVisual(null).TransformBounds(
            new Windows.Foundation.Rect(0, 0, target.ActualWidth, target.ActualHeight));
        return bounds.Width > 0 && bounds.Height > 0 ? bounds : null;
    }

    // Stepping in the loupe moves the grid's selection with it, so the pane
    // shows the card on show and closing lands on it.
    private void Loupe_CurrentChanged(CardBase card)
    {
        // A card with no tile of its own (a character's other version, chosen in
        // the pane): the pane shows it all the same, or its version list would
        // keep marking the card shown before (user report).
        if (EntryOf(card) is not { } entry)
        {
            _inspector.Show([card]);
            return;
        }
        if (GalleryGrid.SelectedItems.Count == 1 && ReferenceEquals(GalleryGrid.SelectedItems[0], entry) && _inspector.Cards is [var shown] && ReferenceEquals(shown, card))
            return;
        _restoringSelection = true;
        try
        {
            GalleryGrid.SelectedItems.Clear();
            GalleryGrid.SelectedItems.Add(entry);
        }
        finally { _restoringSelection = false; }
        // A group's tile stands for all its cards, but the pane shows the one on show.
        _inspector.Show([card]);
    }

    private void Loupe_Closed(CardBase? card)
    {
        _inspector.ShowPreview = true;
        SetLoupeLayout(open: false);
        if (card is null || EntryOf(card) is not { } entry) return;
        GalleryGrid.ScrollIntoView(entry);
        // Programmatic, not Keyboard: the keyboard focus rectangle drew a loud
        // white frame around the tile after every close (live check, wave 16).
        DispatcherQueue.TryEnqueue(() => (GalleryGrid.ContainerFromItem(entry) as Control)?.Focus(FocusState.Programmatic));
    }

    private GalleryEntry? EntryOf(CardBase card)
        => _items.FirstOrDefault(entry => entry.Members.Any(member => ReferenceEquals(member, card)))
            ?? _items.FirstOrDefault(entry => entry.Members.Any(member =>
                string.Equals(member.FilePath, card.FilePath, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// The cards a right-click acts on: the whole selection when the clicked
    /// tile is part of it, otherwise only that tile (as Explorer does).
    /// </summary>
    private IReadOnlyList<CardBase> MenuTargets(GalleryEntry clicked)
        => GalleryGrid.SelectedItems.Count > 1 && GalleryGrid.SelectedItems.Contains(clicked)
            ? SelectedCards()
            : clicked.Members;
}

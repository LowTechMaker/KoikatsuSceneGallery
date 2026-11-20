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
    private const double InspectorMinBrowserWidth = 820;
    private const double InspectorWidth = 320;

    private CardInspector _inspector = null!;
    private bool _restoringSelection;
    private bool _wideEnoughForInspector = true;

    private void InitInspector()
    {
        _inspector = new CardInspector
        {
            Width = InspectorWidth,
            Margin = new Thickness(16, 4, 0, 16),
            Frame = () => _frame,
        };
        Grid.SetRow(_inspector, 2);
        Grid.SetColumn(_inspector, 1);
        RootLayout.Children.Add(_inspector);
        _inspector.OpenRequested += card => OpenDetail(card);

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

    /// <summary>
    /// The cards a right-click acts on: the whole selection when the clicked
    /// tile is part of it, otherwise only that tile (as Explorer does).
    /// </summary>
    private IReadOnlyList<CardBase> MenuTargets(GalleryEntry clicked)
        => GalleryGrid.SelectedItems.Count > 1 && GalleryGrid.SelectedItems.Contains(clicked)
            ? SelectedCards()
            : clicked.Members;
}

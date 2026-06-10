using System.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class GalleryPage : Page
{
    public GalleryViewModel ViewModel { get; }

    private readonly List<WeakReference<TextBlock>> _fileNameTexts = [];

    // Thumbnail size picker, three fixed steps (small / medium / large). The
    // chosen size is a desired card width — we pick the column count that fits
    // and size the ItemsWrapGrid cells so a whole number of columns fills the row
    // exactly (no right-edge whitespace, image keeps the 240:135 ratio). Columns
    // reflow with the window. Card stretches to fill its cell; the cell overhead
    // is a known constant (card Margin, container Margin/Padding = 0), so no live
    // measurement is needed. See ctrl-zippy-plum.
    private const double ImageRatio = 135.0 / 240.0;
    private const double CardMargin = 4;        // gap between a card and its cell edge (XAML CardRoot Margin)
    private const double CardInset = 4 + 1;     // CardRoot Padding + BorderThickness, per side
    private const double CellOverheadW = CardMargin * 2;                 // extra cell width beyond the card
    private const double ContentInsetW = (CardMargin + CardInset) * 2;   // cell width lost before the image
    private const double FilenameReserve = 30;  // approximate filename row height when shown

    // The three fixed sizes: small → medium → large (card width px).
    private static readonly double[] SizePresets = [170, 240, 360];
    private const int MediumIndex = 1;     // default / fallback when the selector is off

    private int _sizeIndex = MediumIndex;  // current preset
    private double _thumbnailWidth = 240;  // effective desired width = preset clamped to the min-columns cap
    private DispatcherTimer? _saveTimer;
    private bool _wheelHooked;
    private int _appliedColumns = -1;        // last column count pushed to the panel
    private double _appliedAvailable = -1;    // panel width at that time

    public GalleryPage()
    {
        ViewModel = App.GalleryViewModel;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _sizeIndex = NearestPresetIndex(App.SettingsViewModel.ThumbnailWidth);
        _thumbnailWidth = SizePresets[_sizeIndex];
        Loaded += OnLoaded;
        App.SettingsViewModel.SceneFolderPathsChanged += OnSceneFolderPathsChanged;
    }

    private void OnSceneFolderPathsChanged()
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            await ViewModel.LoadCardsCommand.ExecuteAsync(null);
        });
    }

    private static int NearestPresetIndex(double width)
    {
        int best = 0;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < SizePresets.Length; i++)
        {
            double diff = Math.Abs(SizePresets[i] - width);
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hook once: the page is cached (NavigationCacheMode.Required) so Loaded
        // can fire on every navigation.
        if (!_wheelHooked)
        {
            GalleryGrid.SizeChanged += GalleryGrid_SizeChanged;
            _wheelHooked = true;
        }
        // The wrap panel isn't ready yet on first load — apply the cache buffer
        // and size the cells once layout settles.
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyCacheLength();
            RefreshSizeSelector();
        });
    }

    /// <summary>
    /// Pushes the user's off-screen render buffer (advanced setting) onto the
    /// wrap panel. Called on load and on every navigation back, so changes made
    /// in Settings take effect when returning to the gallery.
    /// </summary>
    private void ApplyCacheLength()
    {
        if (GalleryGrid.ItemsPanelRoot is ItemsWrapGrid panel)
            panel.CacheLength = App.SettingsViewModel.CacheLength;
    }

    private void GalleryGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Re-clamp the current preset to the new min-columns cap (a narrowing
        // window lowers it; widening restores it) and refit. The persisted value
        // is the preset itself, so resizing never needs a save.
        ApplyDesiredWidth();
    }

    /// <summary>
    /// Picks the column count for the current desired width and sizes the
    /// ItemsWrapGrid cells so those columns fill the row exactly. One property
    /// pair set — the panel reflows its realized cards in a single pass, no
    /// per-card work and no live measurement.
    /// </summary>
    private void ApplyLayout()
    {
        if (GalleryGrid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (_thumbnailWidth + CellOverheadW)));

        // Within a column band the cell size doesn't change, so most wheel ticks
        // need no panel update — skipping them avoids relayout churn that made
        // thumbnails reload and "lag behind" the zoom.
        if (columns == _appliedColumns && available == _appliedAvailable)
            return;
        _appliedColumns = columns;
        _appliedAvailable = available;

        // Shave 0.5px so floating-point never rounds the last column onto the
        // next row.
        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * ImageRatio;
        double filename = App.SettingsViewModel.ShowFileNames ? FilenameReserve : 0;
        double cellH = imageH + filename + (CardMargin + CardInset) * 2;

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    private void SizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: string tag } || !int.TryParse(tag, out int idx))
            return;

        _sizeIndex = Math.Clamp(idx, 0, SizePresets.Length - 1);
        ApplyDesiredWidth();
        ScheduleSave();
        UpdateSizeButtons();
    }

    /// <summary>
    /// Reflects the active size on the three toggle buttons (and re-checks the
    /// active one so it can't be toggled off by clicking it again).
    /// </summary>
    private void UpdateSizeButtons()
    {
        SizeSmallButton.IsChecked = _sizeIndex == 0;
        SizeMediumButton.IsChecked = _sizeIndex == 1;
        SizeLargeButton.IsChecked = _sizeIndex == 2;
    }

    /// <summary>
    /// Sets the desired width from the current preset (or the medium default when
    /// the size selector is disabled), then refits the cells.
    /// </summary>
    private void ApplyDesiredWidth()
    {
        int idx = App.SettingsViewModel.SizeSelectorEnabled ? _sizeIndex : MediumIndex;
        _thumbnailWidth = SizePresets[idx];
        ApplyLayout();
    }

    /// <summary>
    /// Shows or hides the size buttons per the "size selector" setting (an OCD /
    /// fine-tuning toggle) and re-applies the resulting size. Called on load and
    /// on every navigation back, so toggling it in Settings takes effect when
    /// returning to the gallery.
    /// </summary>
    private void RefreshSizeSelector()
    {
        SizeButtonsPanel.Visibility = App.SettingsViewModel.SizeSelectorEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateSizeButtons();
        ApplyDesiredWidth();
    }

    private void ScheduleSave()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += async (_, _) =>
            {
                _saveTimer!.Stop();
                // Persist the chosen preset (not the clamped width) so reload
                // restores the same size step regardless of window width.
                await App.SettingsViewModel.SaveThumbnailWidthAsync(SizePresets[_sizeIndex]);
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // Pick up Settings changes made while we were away (cache buffer + the
        // size-selector toggle).
        ApplyCacheLength();
        RefreshSizeSelector();
        if (ViewModel.Cards.Count == 0)
            await ViewModel.LoadCardsCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GalleryViewModel.ShowFileNames))
        {
            UpdateFileNameVisibility();
            // The filename reserve is part of the cell height but the column
            // count is unchanged — invalidate the skip cache so the refit runs.
            _appliedColumns = -1;
            ApplyLayout();
        }
    }

    private void FileNameText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock tb)
        {
            tb.Visibility = ViewModel.ShowFileNames ? Visibility.Visible : Visibility.Collapsed;
            _fileNameTexts.RemoveAll(wr => !wr.TryGetTarget(out _));
            _fileNameTexts.Add(new WeakReference<TextBlock>(tb));
        }
    }

    private void UpdateFileNameVisibility()
    {
        var visibility = ViewModel.ShowFileNames ? Visibility.Visible : Visibility.Collapsed;
        _fileNameTexts.RemoveAll(wr => !wr.TryGetTarget(out _));
        foreach (var wr in _fileNameTexts)
        {
            if (wr.TryGetTarget(out var tb))
                tb.Visibility = visibility;
        }
    }

    private void GalleryGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is SceneCard recycled)
                ViewModel.ReleaseThumbnail(recycled);
            return;
        }
        if (args.Item is SceneCard card)
            ViewModel.RequestThumbnail(card);

        // Cards size themselves to the cells now (ItemsWrapGrid.ItemWidth/Height),
        // so there's no per-card sizing to do here. Just size the cells + apply
        // the cache buffer once the panel first exists.
        if (_appliedColumns < 0)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ApplyCacheLength();
                ApplyLayout();
            });
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;
    }

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SelectedSort = Enum.Parse<SortOption>(tag);
            ScrollToTop();
        }
    }

    private void GameFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.GameFilter = Enum.Parse<GameFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void EnvironmentFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EnvironmentFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.EnvironmentFilter = Enum.Parse<EnvironmentFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void TimelineFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TimelineFilterComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.TimelineFilter = Enum.Parse<TimelineFilterOption>(tag);
            ScrollToTop();
        }
    }

    private void SortDirectionButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SortAscending = !SortDirectionButton.IsChecked.GetValueOrDefault();
        SortDirectionIcon.Glyph = ViewModel.SortAscending ? "" : "";
        ScrollToTop();
    }

    private void ScrollToTop()
    {
        if (App.SettingsViewModel.ScrollToTopOnSort && ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[0]);
    }

    private async void GalleryGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var files = new List<StorageFile>();
        foreach (var item in e.Items)
        {
            if (item is SceneCard card)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(card.FilePath);
                    files.Add(file);
                }
                catch (Exception) { }
            }
        }
        if (files.Count > 0)
        {
            e.Data.SetStorageItems(files);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void GalleryGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SceneCard card)
        {
            Frame.Navigate(typeof(DetailPage), card);
        }
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.GetRandomCard();
        if (card != null)
            Frame.Navigate(typeof(DetailPage), card);
    }

    private void ScrollToTop_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ScrollToTop();
        args.Handled = true;
    }

    private void ScrollToBottom_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[^1]);
        args.Handled = true;
    }
}
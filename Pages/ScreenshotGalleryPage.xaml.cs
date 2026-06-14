using System.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ScreenshotGalleryPage : Page
{
    public MediaGalleryViewModel ViewModel { get; }

    private readonly List<WeakReference<TextBlock>> _fileNameTexts = [];

    private const double ImageRatio = 135.0 / 240.0;
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;

    private static readonly double[] SizePresets = [170, 240, 360];
    private const int MediumIndex = 1;

    private int _sizeIndex = MediumIndex;
    private double _thumbnailWidth = 240;
    private DispatcherTimer? _saveTimer;
    private bool _wheelHooked;
    private int _appliedColumns = -1;
    private double _appliedAvailable = -1;

    public ScreenshotGalleryPage()
    {
        ViewModel = App.ScreenshotGalleryViewModel;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _sizeIndex = NearestPresetIndex(App.SettingsViewModel.ThumbnailWidth);
        _thumbnailWidth = SizePresets[_sizeIndex];
        Loaded += OnLoaded;
        App.SettingsViewModel.ScreenshotFolderPathsChanged += OnFolderPathsChanged;
    }

    private void OnFolderPathsChanged()
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
            if (diff < bestDiff) { bestDiff = diff; best = i; }
        }
        return best;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_wheelHooked)
        {
            GalleryGrid.SizeChanged += GalleryGrid_SizeChanged;
            _wheelHooked = true;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            ApplyCacheLength();
            RefreshSizeSelector();
        });
    }

    private void ApplyCacheLength()
    {
        if (GalleryGrid.ItemsPanelRoot is ItemsWrapGrid panel)
            panel.CacheLength = App.SettingsViewModel.CacheLength;
    }

    private void GalleryGrid_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyDesiredWidth();

    private void ApplyLayout()
    {
        if (GalleryGrid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (_thumbnailWidth + CellOverheadW)));

        if (columns == _appliedColumns && available == _appliedAvailable)
            return;
        _appliedColumns = columns;
        _appliedAvailable = available;
        ViewModel.SetShuffleCount(columns * 2);

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

    private void UpdateSizeButtons()
    {
        SizeSmallButton.IsChecked = _sizeIndex == 0;
        SizeMediumButton.IsChecked = _sizeIndex == 1;
        SizeLargeButton.IsChecked = _sizeIndex == 2;
    }

    private void ApplyDesiredWidth()
    {
        int idx = App.SettingsViewModel.SizeSelectorEnabled ? _sizeIndex : MediumIndex;
        _thumbnailWidth = SizePresets[idx];
        ApplyLayout();
    }

    private void RefreshSizeSelector()
    {
        SizeButtonsPanel.Visibility = App.SettingsViewModel.SizeSelectorEnabled
            ? Visibility.Visible : Visibility.Collapsed;
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
                await App.SettingsViewModel.SaveThumbnailWidthAsync(SizePresets[_sizeIndex]);
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ApplyCacheLength();
        RefreshSizeSelector();
        if (ViewModel.Cards.Count == 0)
            await ViewModel.LoadCardsCommand.ExecuteAsync(null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaGalleryViewModel.ShowFileNames))
        {
            UpdateFileNameVisibility();
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
            if (args.Item is MediaCard recycled) ViewModel.ReleaseThumbnail(recycled);
            return;
        }
        if (args.Item is MediaCard card) ViewModel.RequestThumbnail(card);

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
        => ViewModel.SearchText = sender.Text;

    private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            ViewModel.SelectedSort = Enum.Parse<SortOption>(tag);
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
        if (GalleryGrid is not null && App.SettingsViewModel.ScrollToTopOnSort && ViewModel.CardsView.Count > 0)
            GalleryGrid.ScrollIntoView(ViewModel.CardsView[0]);
    }

    private async void GalleryGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var files = new List<StorageFile>();
        foreach (var item in e.Items)
        {
            if (item is MediaCard card)
            {
                try { files.Add(await StorageFile.GetFileFromPathAsync(card.FilePath)); }
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
        if (e.ClickedItem is MediaCard card)
            Frame.Navigate(typeof(ScreenshotDetailPage), card);
    }

    private void RandomButton_Click(object sender, RoutedEventArgs e)
    {
        var card = ViewModel.GetRandomCard();
        if (card != null) Frame.Navigate(typeof(ScreenshotDetailPage), card);
    }

    private void FeelingLucky_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Reshuffle();
        ScrollToTop();
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

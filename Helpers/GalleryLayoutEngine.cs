using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace KoikatsuSceneGallery.Helpers;

internal sealed class GalleryLayoutEngine
{
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;
    private const int MediumIndex = 1;
    private static readonly double[] SizePresets = [170, 240, 360];

    private readonly double _imageRatio;
    private readonly GridView _grid;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action<int> _setShuffleDisplayCount;
    private readonly SettingsViewModel _settingsViewModel;

    private int _sizeIndex = MediumIndex;
    private double _thumbnailWidth = 240;
    private DispatcherTimer? _saveTimer;
    private int _appliedColumns = -1;
    private double _appliedAvailable = -1;
    private ScrollViewer? _scrollViewer;
    private Action? _onScrollStop;
    private bool _sizeChangedHooked;

    public GalleryLayoutEngine(
        double imageRatio,
        GridView grid,
        DispatcherQueue dispatcherQueue,
        Action<int> setShuffleDisplayCount,
        SettingsViewModel settingsViewModel)
    {
        _imageRatio = imageRatio;
        _grid = grid;
        _dispatcherQueue = dispatcherQueue;
        _setShuffleDisplayCount = setShuffleDisplayCount;
        _settingsViewModel = settingsViewModel;
        _sizeIndex = NearestPresetIndex(_settingsViewModel.ThumbnailWidth);
        _thumbnailWidth = SizePresets[_sizeIndex];
    }

    public static int NearestPresetIndex(double width)
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

    public void OnLoaded(Action? onScrollStop = null)
    {
        _onScrollStop = onScrollStop;
        if (!_sizeChangedHooked)
        {
            _grid.SizeChanged += (_, _) => ApplyDesiredWidth();
            _sizeChangedHooked = true;
        }

        if (_scrollViewer is null)
        {
            _scrollViewer = VisualTreeSearch.FindDescendant<ScrollViewer>(_grid);
            if (_scrollViewer is not null)
                _scrollViewer.ViewChanged += (_, ev) =>
                {
                    if (!ev.IsIntermediate)
                        _onScrollStop?.Invoke();
                };
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            ApplyCacheLength();
            RefreshSizeSelector(null);
        });
    }

    public void ApplyCacheLength()
    {
        if (_grid.ItemsPanelRoot is ItemsWrapGrid panel)
            panel.CacheLength = _settingsViewModel.CacheLength;
    }

    public void ApplyLayout()
    {
        if (_grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (_thumbnailWidth + CellOverheadW)));

        if (columns == _appliedColumns && available == _appliedAvailable)
            return;
        _appliedColumns = columns;
        _appliedAvailable = available;
        _setShuffleDisplayCount(columns * 2);

        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * _imageRatio;
        double filename = _settingsViewModel.ShowFileNames ? FilenameReserve : 0;
        double cellH = imageH + filename + (CardMargin + CardInset) * 2;

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    public void InvalidateAndRefit()
    {
        _appliedColumns = -1;
        ApplyLayout();
    }

    public void EnsureLayoutOnFirstContent()
    {
        if (_appliedColumns < 0)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ApplyCacheLength();
                ApplyLayout();
            });
        }
    }

    public void HandleSizeButtonClick(int idx)
    {
        _sizeIndex = Math.Clamp(idx, 0, SizePresets.Length - 1);
        ApplyDesiredWidth();
        ScheduleSave();
    }

    public void UpdateSizeButtons(ToggleButton small, ToggleButton medium, ToggleButton large)
    {
        small.IsChecked = _sizeIndex == 0;
        medium.IsChecked = _sizeIndex == 1;
        large.IsChecked = _sizeIndex == 2;
    }

    public void ApplyDesiredWidth()
    {
        int idx = _settingsViewModel.SizeSelectorEnabled ? _sizeIndex : MediumIndex;
        _thumbnailWidth = SizePresets[idx];
        ApplyLayout();
    }

    public void RefreshSizeSelector(StackPanel? sizeButtonsPanel)
    {
        if (sizeButtonsPanel != null)
        {
            sizeButtonsPanel.Visibility = _settingsViewModel.SizeSelectorEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
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
                await _settingsViewModel.SaveThumbnailWidthAsync(SizePresets[_sizeIndex]);
            };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}

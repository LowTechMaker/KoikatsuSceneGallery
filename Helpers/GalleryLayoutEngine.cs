using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Helpers;

internal sealed class GalleryLayoutEngine
{
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;
    private double _baseItemWidth = 240;

    private readonly double _imageRatio;
    private readonly GridView _grid;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Action<int> _setShuffleDisplayCount;
    private readonly SettingsViewModel _settingsViewModel;
    private double? _metadataReserve;

    private int _appliedColumns = -1;
    private double _appliedAvailable = -1;
    private ScrollViewer? _scrollViewer;
    private Action? _onScrollStop;
    private bool _sizeChangedHooked;
    private ItemsWrapGrid? _hookedPanel;

    public GalleryLayoutEngine(
        double imageRatio,
        GridView grid,
        DispatcherQueue dispatcherQueue,
        Action<int> setShuffleDisplayCount,
        SettingsViewModel settingsViewModel,
        double? metadataReserve = null)
    {
        _imageRatio = imageRatio;
        _grid = grid;
        _dispatcherQueue = dispatcherQueue;
        _setShuffleDisplayCount = setShuffleDisplayCount;
        _settingsViewModel = settingsViewModel;
        _metadataReserve = metadataReserve;
    }

    public void OnLoaded(Action? onScrollStop = null)
    {
        _onScrollStop = onScrollStop;
        if (!_sizeChangedHooked)
        {
            _grid.SizeChanged += (_, _) => ApplyLayout();
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
            ApplyLayout();
        });
    }

    public double BaseItemWidth
    {
        get => _baseItemWidth;
        set
        {
            if (Math.Abs(_baseItemWidth - value) < 0.01)
                return;

            _baseItemWidth = value;
            InvalidateAndRefit();
        }
    }

    public void ApplyCacheLength()
    {
        if (_grid.ItemsPanelRoot is ItemsWrapGrid panel)
            panel.CacheLength = _settingsViewModel.CacheLength;
    }

    public void SetMetadataReserve(double height)
    {
        if (_metadataReserve == height) return;
        _metadataReserve = height;
        InvalidateAndRefit();
    }

    /// <remarks>
    /// The items panel can be asked before it has been measured (a library tab
    /// opened for the first time, its cards still arriving). That used to give
    /// up and wait for the grid itself to change size, and meanwhile every tile
    /// took the size of its loading placeholder: a field of tiny icons for a
    /// few seconds (user report). Now the grid's width stands in for the
    /// panel's, a default size is set when neither is known, and the panel's
    /// own first size change applies the real layout.
    /// </remarks>
    public void ApplyLayout()
    {
        if (_grid.ItemsPanelRoot is not ItemsWrapGrid panel)
            return;
        HookPanel(panel);

        double available = panel.ActualWidth > 0 ? panel.ActualWidth : _grid.ActualWidth;
        if (available <= 0)
        {
            ApplyProvisional(panel);
            return;
        }
        int columns = Math.Max(1, (int)Math.Floor(available / (_baseItemWidth + CellOverheadW)));

        if (columns == _appliedColumns && available == _appliedAvailable)
            return;
        _appliedColumns = columns;
        _appliedAvailable = available;
        _setShuffleDisplayCount(columns * 2);

        double cellW = (available / columns) - 0.5;
        panel.ItemWidth = cellW;
        panel.ItemHeight = CellHeight(cellW);
    }

    private double CellHeight(double cellW)
    {
        double imageH = Math.Max(0, cellW - ContentInsetW) * _imageRatio;
        double metadata = _metadataReserve ?? (_settingsViewModel.ShowFileNames ? FilenameReserve : 0);
        return imageH + metadata + (CardMargin + CardInset) * 2;
    }

    // Before anything has been measured: tiles of the chosen size, not of their placeholders.
    private void ApplyProvisional(ItemsWrapGrid panel)
    {
        if (!double.IsNaN(panel.ItemWidth)) return;
        double cellW = _baseItemWidth + CellOverheadW;
        panel.ItemWidth = cellW;
        panel.ItemHeight = CellHeight(cellW);
    }

    private void HookPanel(ItemsWrapGrid panel)
    {
        if (ReferenceEquals(_hookedPanel, panel)) return;
        _hookedPanel = panel;
        panel.SizeChanged += (_, _) => ApplyLayout();
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

}

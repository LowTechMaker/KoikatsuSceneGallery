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

    public void ApplyLayout()
    {
        if (_grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (_baseItemWidth + CellOverheadW)));

        if (columns == _appliedColumns && available == _appliedAvailable)
            return;
        _appliedColumns = columns;
        _appliedAvailable = available;
        _setShuffleDisplayCount(columns * 2);

        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * _imageRatio;
        double metadata = _metadataReserve ?? (_settingsViewModel.ShowFileNames ? FilenameReserve : 0);
        double cellH = imageH + metadata + (CardMargin + CardInset) * 2;

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

}

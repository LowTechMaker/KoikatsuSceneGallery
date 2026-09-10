using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Sizes author live tiles so a row always fills the page. A desktop window
/// ranges from a narrow side-by-side (~600px of content) to a maximized
/// ultrawide (~2500px), so any single fixed tile width leaves a ragged gap at
/// most of the widths in between: the tile has to flex, the column count has
/// to follow the window.
/// </summary>
internal static class AuthorTileLayout
{
    /// <summary>Below this the name plus three counters stop fitting on one line.</summary>
    private const double MinTileWidth = 260;

    /// <summary>Above this a tile holding one name reads as an empty banner.</summary>
    private const double MaxTileWidth = 340;

    /// <summary>Margin the item templates put on each side of a tile.</summary>
    private const double TileMargin = 4;

    private const double CellOverhead = TileMargin * 2;

    /// <summary>The original 280x160 proportion, kept as tiles grow.</summary>
    private const double AspectRatio = 280d / 160d;

    private const double MinTileHeight = 150;
    /// <summary>
    /// A live tile grows with its width, but past this it starts to dominate
    /// the page: at a windowed width the rows read as too tall long before
    /// the tile itself looks wrong.
    /// </summary>
    private const double MaxTileHeight = 180;

    /// <summary>
    /// Recomputes the cell size for a tile grid. Safe to call on both Loaded
    /// and SizeChanged; it is a no-op until the panel has a width.
    /// </summary>
    public static void Apply(GridView grid)
    {
        if (grid.ItemsPanelRoot is ItemsWrapGrid { ActualWidth: > 0 })
        {
            ApplyCore(grid);
            return;
        }

        // The panel only exists once the first container is realized, which
        // can land after the size change that brought us here.
        grid.DispatcherQueue.TryEnqueue(() => ApplyCore(grid));
    }

    private static void ApplyCore(GridView grid)
    {
        // The panel's own width is the viewport: unlike the GridView's, it
        // already excludes the vertical scroll bar, so a row we size to it
        // actually fits instead of wrapping one tile early.
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        var available = panel.ActualWidth;

        // Fewest columns that keep tiles at or under the maximum, then capped
        // by the most columns that still keep them at or over the minimum.
        var columns = (int)Math.Ceiling(available / (MaxTileWidth + CellOverhead));
        var maxColumns = (int)Math.Floor(available / (MinTileWidth + CellOverhead));
        columns = Math.Clamp(columns, 1, Math.Max(1, maxColumns));

        // The half pixel keeps rounding from pushing the last tile onto the
        // next row.
        var cellWidth = (available / columns) - 0.5;
        var tileHeight = Math.Clamp((cellWidth - CellOverhead) / AspectRatio, MinTileHeight, MaxTileHeight);

        panel.ItemWidth = cellWidth;
        panel.ItemHeight = Math.Round(tileHeight) + CellOverhead;
    }
}

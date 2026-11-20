namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How many of a home shelf's tiles fit on its first few lines. The shelf wraps
/// its tiles like a gallery instead of scrolling them sideways, and shows only
/// the first two lines until the user asks for all of them.
/// </summary>
/// <remarks>
/// Worked out from the tiles' own widths (each tile's width follows its card's
/// aspect ratio), so the shelf can leave the rest out of the tree altogether:
/// hiding them inside the panel would still let Tab reach them, and they would
/// still ask for thumbnails.
/// </remarks>
internal static class ShelfLines
{
    /// <summary>The number of tiles, from the start, that fit on at most <paramref name="maxLines"/> lines.</summary>
    /// <param name="widths">Each tile's width, in order.</param>
    /// <param name="available">The width the shelf has. Zero or less (not measured yet) counts as one tile per line.</param>
    /// <param name="spacing">Between two tiles on a line.</param>
    /// <param name="maxLines">How many lines; zero or less means no limit.</param>
    public static int Fit(IReadOnlyList<double> widths, double available, double spacing, int maxLines)
    {
        if (maxLines <= 0) return widths.Count;
        var lines = 1;
        double line = 0;
        for (var i = 0; i < widths.Count; i++)
        {
            var width = widths[i];
            // The first tile on a line always goes there, however wide it is.
            if (line > 0 && line + spacing + width > available)
            {
                if (++lines > maxLines) return i;
                line = width;
            }
            else
            {
                line = line > 0 ? line + spacing + width : width;
            }
        }
        return widths.Count;
    }
}

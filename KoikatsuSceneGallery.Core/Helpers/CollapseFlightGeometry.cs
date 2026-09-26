namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// A rectangle in some shared coordinate space. Declared here rather than using
/// <c>Windows.Foundation.Rect</c> so the geometry stays in Core, where it can be
/// tested without a UI thread.
/// </summary>
public readonly record struct FlightRect(double X, double Y, double Width, double Height)
{
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}

/// <summary>
/// Where each card has to travel, and when, for a group to collapse back onto
/// its stacked tile.
/// </summary>
public static class CollapseFlightGeometry
{
    /// <summary>How far this tile's centre is from the target's, in the shared space.</summary>
    public static (double X, double Y) CenterDelta(FlightRect origin, FlightRect target)
        => (target.CenterX - origin.CenterX, target.CenterY - origin.CenterY);

    /// <summary>
    /// The uniform shrink that fits the origin inside the target. Degenerate
    /// rectangles keep their size rather than collapsing to nothing or dividing
    /// by zero.
    /// </summary>
    public static double UniformScale(FlightRect origin, FlightRect target)
        => origin.Width <= 0 || origin.Height <= 0
            ? 1
            : Math.Min(target.Width / origin.Width, target.Height / origin.Height);

    /// <summary>
    /// Per-item delay, shrunk so the whole flight fits inside
    /// <paramref name="budget"/> however many tiles are on screen. Without the
    /// budget a full viewport would still be launching cards long after the
    /// first ones landed.
    /// </summary>
    public static TimeSpan StaggerFor(int count, TimeSpan perItem, TimeSpan budget)
        => count <= 1
            ? TimeSpan.Zero
            : TimeSpan.FromTicks(Math.Min(perItem.Ticks, budget.Ticks / (count - 1)));

    /// <summary>
    /// Indices ordered nearest-to-target first, so the collapse reads as one
    /// movement inwards rather than a scatter. Ties keep their original order.
    /// </summary>
    public static int[] OrderByDistance(IReadOnlyList<FlightRect> origins, FlightRect target)
        => [.. Enumerable.Range(0, origins.Count).OrderBy(i => SquaredDistance(origins[i], target))];

    private static double SquaredDistance(FlightRect origin, FlightRect target)
    {
        var (dx, dy) = CenterDelta(origin, target);
        return dx * dx + dy * dy;
    }
}

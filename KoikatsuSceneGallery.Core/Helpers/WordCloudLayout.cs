namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Places words in the blob shape a word cloud is recognised by.
/// </summary>
/// <remarks>
/// A wrapping panel cannot produce this. Whatever the order or the line
/// heights, wrapping fills every line to the full width, so the silhouette is
/// always a rectangle — it reads as a paragraph of mixed-size text, not a
/// cloud. The shape has to come from placement.
///
/// The classic algorithm, and the one used here: take the words largest first,
/// walk an Archimedean spiral outwards from the centre, and drop each word at
/// the first position where it touches nothing already placed. Big words land
/// near the middle, small ones fill the gaps around them, and the outline ends
/// up ragged and roughly elliptical.
///
/// Pure rectangle arithmetic — the caller measures the text once and passes
/// sizes in. With a bounded word count this costs microseconds.
/// </remarks>
public static class WordCloudLayout
{
    /// <summary>
    /// Horizontal stretch of the spiral. Clouds read better wider than tall,
    /// and it matches the shape of the space they are usually given.
    /// </summary>
    private const double AspectRatio = 2.2;

    private const double AngleStep = 0.12;
    private const double RadiusPerRadian = 0.55;

    /// <summary>Breathing room between neighbours, so the mass is tight but not touching.</summary>
    public const double DefaultPadding = 6;

    /// <summary>
    /// Positions for <paramref name="sizes"/>, in the same order. An entry is
    /// null when that word found nowhere to go, which the caller should treat
    /// as "do not show it" rather than as a failure.
    /// </summary>
    /// <param name="sizes">Measured sizes, largest first.</param>
    public static IReadOnlyList<FlightRect?> Place(
        IReadOnlyList<(double Width, double Height)> sizes,
        double canvasWidth,
        double canvasHeight,
        double padding = DefaultPadding)
    {
        var placed = new List<FlightRect>(sizes.Count);
        var results = new FlightRect?[sizes.Count];
        if (canvasWidth <= 0 || canvasHeight <= 0) return results;

        var centerX = canvasWidth / 2;
        var centerY = canvasHeight / 2;
        // Far enough to reach any corner of the canvas.
        var maxRadius = Math.Sqrt(canvasWidth * canvasWidth + canvasHeight * canvasHeight);

        for (var i = 0; i < sizes.Count; i++)
        {
            var (width, height) = sizes[i];
            if (width <= 0 || height <= 0 || width > canvasWidth || height > canvasHeight) continue;

            for (var step = 0; ; step++)
            {
                var angle = step * AngleStep;
                var radius = RadiusPerRadian * angle;
                if (radius > maxRadius) break;

                var x = centerX + radius * AspectRatio * Math.Cos(angle) - width / 2;
                var y = centerY + radius * Math.Sin(angle) - height / 2;
                var candidate = new FlightRect(x, y, width, height);

                if (!Contains(candidate, canvasWidth, canvasHeight)) continue;
                if (Overlaps(placed, candidate, padding)) continue;

                placed.Add(candidate);
                results[i] = candidate;
                break;
            }
        }

        return results;
    }

    private static bool Contains(FlightRect rect, double width, double height)
        => rect.X >= 0 && rect.Y >= 0 && rect.X + rect.Width <= width && rect.Y + rect.Height <= height;

    private static bool Overlaps(List<FlightRect> placed, FlightRect candidate, double padding)
    {
        foreach (var other in placed)
        {
            if (candidate.X < other.X + other.Width + padding
                && other.X < candidate.X + candidate.Width + padding
                && candidate.Y < other.Y + other.Height + padding
                && other.Y < candidate.Y + candidate.Height + padding)
            {
                return true;
            }
        }
        return false;
    }
}

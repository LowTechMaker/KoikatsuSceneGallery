namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How big the home page's two "this or that" cards are: one section among
/// the others, not a banner. The two together are at most <see cref="MaxWidthBoth"/>
/// wide and the whole panel at most about a third of the window's height.
/// </summary>
/// <remarks>
/// Seen live at 1440x753: sized by width alone, the panel filled the page and
/// every shelf fell below the fold. Capped at half the height it still read as
/// the hero banner of a TV app, so both caps were tightened. The height cap
/// shrinks both cards together and keeps them 16:9; a minimum keeps them worth
/// looking at in a low window.
/// </remarks>
internal static class DuelLayout
{
    /// <summary>The two cards together are never wider than this.</summary>
    public const double MaxWidthBoth = 560;
    /// <summary>Below this panel width the two cards stack.</summary>
    public const double StackBelow = 480;
    /// <summary>The part of the window's height the whole panel may take.</summary>
    public const double HeightShare = 1.0 / 3;
    /// <summary>Between the two cards.</summary>
    public const double Gap = 16;
    /// <summary>The smallest picture height, however low the window.</summary>
    public const double MinPictureHeight = 120;

    /// <param name="panelWidth">The width the panel has.</param>
    /// <param name="windowHeight">The window's height.</param>
    /// <param name="chrome">The panel's height without its pictures: heading, hint, captions, skip button, spacing.</param>
    public static (bool Stacked, double CardWidth, double PictureHeight) Fit(double panelWidth, double windowHeight, double chrome)
    {
        var stacked = panelWidth < StackBelow;
        var available = Math.Min(Math.Max(panelWidth, 0), MaxWidthBoth);
        var byWidth = (stacked ? available : (available - Gap) / 2) * 9 / 16;
        var pictures = stacked ? 2 : 1;
        var byHeight = (windowHeight * HeightShare - chrome) / pictures;
        var height = Math.Max(MinPictureHeight, Math.Min(byWidth, byHeight));
        return (stacked, height * 16 / 9, height);
    }
}

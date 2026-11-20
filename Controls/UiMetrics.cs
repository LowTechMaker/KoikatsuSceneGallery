using Microsoft.UI.Xaml;

namespace KoikatsuSceneGallery.Controls;

/// <summary>
/// The app's one place for sizes and spacing, as <see cref="CardMotion"/> is for
/// motion and <c>Styles/Tokens.xaml</c> for colours and corners.
/// </summary>
/// <remarks>
/// The desktop overhaul (waves 11-17 and after) wrote its numbers where it
/// used them: the details pane's width in four places, the gap beside it in
/// three, the stack of cards' angles in a control and again in a template.
/// The user asked for them to be managed in one place, as the animations had
/// been; every such number now lives here, named for what it is, and the
/// controls and templates read it (templates through x:Bind).
/// </remarks>
internal static class UiMetrics
{
    // ------------------------------------------------------------ panes beside content

    /// <summary>The details pane (CardInspector) beside a grid, a shelf page or the viewer. 360, not 320: the user found it narrow.</summary>
    public const double InspectorWidth = 360;
    /// <summary>The authors page's details pane (AuthorInspector): the same width as the cards' pane.</summary>
    public const double AuthorPaneWidth = InspectorWidth;
    /// <summary>Below this width a host hides its pane rather than squeeze the content to a column or two.</summary>
    public const double InspectorMinHostWidth = 820;
    /// <summary>Between a pane and what is beside it.</summary>
    public const double PaneGap = 16;
    /// <summary>Beside the library grid, whose scroll-bar gutter already makes most of the gap.</summary>
    public const double PaneGapBesideGrid = 4;
    /// <summary>Under a pane, above the window's bottom edge.</summary>
    public const double PaneBottom = 16;
    /// <summary>Inside a pane, around its content.</summary>
    public const double PanePadding = 16;
    /// <summary>The picture at the top of the details pane.</summary>
    public const double InspectorPreviewHeight = 180;

    public static Thickness InspectorMargin(double leftGap) => new(leftGap, 0, 0, PaneBottom);

    // ------------------------------------------------------------ the library grid

    /// <summary>Right padding of the grid: its overlay scroll bar runs there, not over the pictures.</summary>
    public const double GridScrollGutter = 16;
    public static Thickness GridPadding => new(0, 0, GridScrollGutter, 0);

    /// <summary>Photo mode (file names off): half the gap between two pictures.</summary>
    public const double PhotoTileMargin = 1;
    /// <summary>Card mode (file names on): half the gap between two cards, and the picture's inset in its card.</summary>
    public const double CardTileMargin = 4;
    public const double CardTileInset = 6;
    /// <summary>The picture's corners inside a card (square edge to edge in photo mode).</summary>
    public const double CardPictureCorner = 4;
    /// <summary>Card mode's caption: title, then author and badges.</summary>
    public const double CardCaptionHeight = 58;
    /// <summary>The plainer two-line caption random discovery puts under its pictures.</summary>
    public const double SimpleCaptionHeight = 44;

    // ------------------------------------------------------------ a stack of cards (a group's tile, several selected)

    /// <summary>How far each card behind the front one is turned, shifted and faded, per step back.</summary>
    public const double StackTurnStep = 3;
    public const double StackShiftXStep = 5;
    public const double StackShiftYStep = -3;
    public const double StackFadeStep = 0.12;
    /// <summary>Cards drawn in the details pane's stack of a selection.</summary>
    public const int StackShown = 4;
    /// <summary>Room around a group's cover on its tile for the cards turned out behind it.</summary>
    public static Thickness GroupCoverInset => new(4, 10, 16, 6);

    // The tile template's two cards behind a group's cover (x:Bind reads these).
    public static double StackNearTurn => StackTurnStep;
    public static double StackNearShiftX => StackShiftXStep;
    public static double StackNearShiftY => StackShiftYStep;
    public static double StackNearOpacity => 1 - StackFadeStep;
    public static double StackFarTurn => StackTurnStep * 2;
    public static double StackFarShiftX => StackShiftXStep * 2;
    public static double StackFarShiftY => StackShiftYStep * 2;
    public static double StackFarOpacity => 1 - StackFadeStep * 2;

    // ------------------------------------------------------------ home shelves and the author overview

    public const double ShelfTileHeight = 135;
    public const double ShelfTileSpacing = 8;
    /// <summary>Lines a home shelf or an author-overview section shows before "show all".</summary>
    public const int CollapsedLines = 2;

    // ------------------------------------------------------------ the loupe

    /// <summary>The most a picture is enlarged to fit: beyond twice its own pixels it is only blur.</summary>
    public const double LoupeMaxFitScale = 2;
    public const float LoupeMaxZoom = 8;
    /// <summary>Zoom per wheel notch, and per +/- press or button.</summary>
    public const double LoupeZoomStep = 1.2;
    /// <summary>The widest the picture is decoded at.</summary>
    public const int LoupeDecodeWidth = 2560;
    public const double LoupeEdgeButtonSize = 44;
    public static Thickness LoupePadding => new(16, 12, 16, 12);

    // ------------------------------------------------------------ responsive breakpoints (all in code; there are no AdaptiveTriggers)

    /// <summary>Below this a page drops to one column: the library's toolbar wraps, the post page stacks, the title bar's origin selector compacts.</summary>
    public const double WideLayoutWidth = 900;
    /// <summary>Below this settings and import switch to their narrow layout.</summary>
    public const double NarrowLayoutWidth = 720;
    /// <summary>Random discovery's own narrow layout.</summary>
    public const double DiscoveryNarrowWidth = 640;

    // ------------------------------------------------------------ pictures and icons drawn in code

    /// <summary>A gallery thumbnail's decode width (tiles, the duel's stand-in, the loupe's stand-in, live tiles).</summary>
    public const int ThumbnailDecodeWidth = 400;
    /// <summary>An icon inside a button (the Tokens.xaml IconSize, for code).</summary>
    public const double IconSize = 14;
    /// <summary>A progress ring beside a line of text (the Tokens.xaml RingSize, for code).</summary>
    public const double RingSize = 16;

    // ------------------------------------------------------------ a selector that is the page's title (library kinds, author platforms)

    /// <summary>The selected entry is the title (PageTitleTextBlockStyle's size); the others small and quiet.</summary>
    public const double SelectorTitleFontSize = 20;
    public const double SelectorOtherFontSize = 14;
    public const double SelectorOtherOpacity = 0.6;

    // ------------------------------------------------------------ the authors page

    /// <summary>Cards shown in the authors page's details pane, in rows of <see cref="AuthorPanePreviewColumns"/>.</summary>
    public const int AuthorPanePreviews = 9;
    public const int AuthorPanePreviewColumns = 3;
    public const double AuthorPanePreviewGap = 6;
    public const double AuthorAvatarSize = 56;
}

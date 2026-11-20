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

    // ------------------------------------------------------------ the home page (a dashboard of panels)

    /// <summary>A panel is never narrower than this; the page fits as many per row as this allows, at most <see cref="HomePanelColumns"/>.</summary>
    public const double HomePanelMinWidth = 380;
    public const int HomePanelColumns = 3;
    /// <summary>Between two panels, and between the top band and the panels.</summary>
    public const double HomePanelGap = 12;
    /// <summary>Between the thumbnails inside a panel's mosaic.</summary>
    public const double HomeMosaicGap = 4;
    /// <summary>A panel's mosaic: one large picture and four small ones in two rows beside it.</summary>
    public const int HomeMosaicCards = 5;
    /// <summary>A compact panel (beside the featured card): one row of this many pictures.</summary>
    public const int HomeMosaicCompactCards = 3;
    /// <summary>
    /// The column beside the featured card: the library's numbers, then the
    /// queued, recent and liked panels under them (user request). As wide as
    /// a panel at its least, so those panels look the same as the others.
    /// </summary>
    public const double HomeSideColumnWidth = HomePanelMinWidth;
    /// <summary>The featured card is not put beside the side column when it would be narrower than this.</summary>
    public const double HomeFeaturedMinWidth = 560;
    /// <summary>The featured picture's largest width, and the width it is decoded at.</summary>
    public const double HomeFeaturedMaxPictureWidth = 960;
    public const int HomeFeaturedDecodeWidth = 1280;
    /// <summary>How see-through the featured picture is behind the whole top band.</summary>
    public const double HomeFeaturedBackdropOpacity = 0.16;
    /// <summary>How many of the dealt hand the featured card turns through, each a small picture in the strip under it.</summary>
    public const int HomeFeaturedCount = 6;
    public const double HomeFeaturedStripWidth = 64;
    public const int HomeFeaturedStripDecodeWidth = 128;
    /// <summary>Tags named under the featured card's title.</summary>
    public const int HomeFeaturedTags = 4;
    /// <summary>"New this week" counts files added within this many days.</summary>
    public const int HomeNewDays = 7;

    // ------------------------------------------------------------ random discovery's card holder (a hand on a three-tier rack)

    /// <summary>Tiers on the rack; the back one holds the most cards, as a real one does.</summary>
    public const int HolderTiers = 3;
    /// <summary>A card's height for its width (the gallery's thumbnails, 135 by 240).</summary>
    public const double HolderCardAspect = 135.0 / 240;
    /// <summary>The largest a card is drawn, however much room there is.</summary>
    public const double HolderMaxCardWidth = 420;
    /// <summary>Each next card's left edge, as a share of a card's width: the rest overlaps.</summary>
    public const double HolderStep = 0.74;
    /// <summary>How far each tier sits below the one behind it, as a share of a card's height.</summary>
    public const double HolderTierDrop = 0.42;
    /// <summary>The fan: degrees of turn per card away from the middle of its tier, and the dip along the arc.</summary>
    public const double HolderFanDegrees = 2.2;
    public const double HolderArc = 0.025;
    /// <summary>How much of the front tier the tray hides, and how far it reaches below them.</summary>
    public const double HolderTrayCover = 0.22;
    public const double HolderTrayBelow = 0.16;
    public const double HolderTrayPadding = 28;
    public const double HolderTrayCorner = 18;
    /// <summary>How far a card is drawn up out of the rack under the pointer or focus, as a share of its height.</summary>
    public const double HolderLift = 0.30;
    public const double HolderCardCorner = 6;

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

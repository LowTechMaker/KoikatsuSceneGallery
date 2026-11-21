namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// A searchable place on the settings page: a card, or the heading of a
/// section that has no card of its own (the folder lists, the filters).
/// </summary>
/// <param name="Uid">The element's <c>x:Uid</c>.</param>
/// <param name="Tab">The settings tab it is on, from 0.</param>
/// <param name="IsCard">A card (label <c>.Header</c>) or a section heading (<c>.Text</c>).</param>
public sealed record SettingsSearchEntry(string Uid, int Tab, bool IsCard)
{
    public string LabelKey => Uid + (IsCard ? ".Header" : ".Text");

    /// <summary>A card's description, searched as keywords. Headings have none.</summary>
    public string? DescriptionKey => IsCard ? Uid + ".Description" : null;
}

/// <summary>
/// Every place on the settings page the command palette can jump to.
/// </summary>
/// <remarks>
/// Written out rather than discovered: the page is compiled, and building one
/// just to read its cards would load every setting's control. A test parses
/// <c>SettingsPage.xaml</c> and fails when a card or heading is added, moved or
/// removed without this list following, so it cannot quietly fall behind.
/// </remarks>
public static class SettingsSearchIndex
{
    public const int General = 0, Folders = 1, Filters = 2, Advanced = 3, Plugins = 4, Experimental = 5;

    /// <summary>The tab headings' keys, by tab.</summary>
    public static IReadOnlyList<string> TabKeys { get; } =
    [
        "Settings_TabGeneral.Header",
        "Settings_TabFolders.Header",
        "Settings_TabFilters.Header",
        "Settings_TabAdvanced.Header",
        "Settings_TabPlugins.Header",
        "Settings_TabOcd.Header",
    ];

    public static IReadOnlyList<SettingsSearchEntry> Entries { get; } =
    [
        Card("SC_ShowFileNames", General),
        Card("SC_RatingSource", General),
        Card("SC_RatingFilterEnabled", General),
        Card("SC_ScrollToTop", General),
        Card("SC_DiscoverySpread", General),
        Card("SC_PokerHand", General),
        Card("SC_GroupCollapseAnimation", General),
        Card("SC_AuthorLiveTiles", General),
        Card("SC_Navigation", General),
        Card("SC_Language", General),
        Heading("Settings_DisplayHeader", General),
        Heading("Settings_NavigationHeader", General),
        Heading("Settings_LanguageHeader", General),

        Heading("Settings_FoldersHeader", Folders),
        Heading("Settings_CharaFoldersHeader", Folders),
        Heading("Settings_CoordFoldersHeader", Folders),
        Heading("Settings_ModsHeader", Folders),
        Card("SC_KoikatsuGameFolder", Folders),
        Card("SC_KoikatsuSunshineGameFolder", Folders),
        Card("SC_ModInstallFolderName", Folders),
        Card("SC_LayoutMigration", Folders),

        Heading("Settings_SizeFiltersHeader", Filters),
        Heading("Settings_SceneResolutionHeader", Filters),
        Heading("Settings_CharaResolutionHeader", Filters),
        Heading("Settings_CoordResolutionHeader", Filters),
        Heading("Settings_SceneTagsHeader", Filters),
        Heading("Settings_PluginHeader", Filters),

        Heading("Settings_CacheHeader", Advanced),
        Heading("Settings_AdvancedHeader", Advanced),
        Card("SC_CacheFolder", Advanced),
        Card("SC_CacheUsage", Advanced),
        Card("SC_CacheActions", Advanced),
        Card("SC_CacheBuffer", Advanced),

        Heading("Plugins_Header", Plugins),

        Card("SC_VisualSimilarity", Experimental),
        Card("SC_ThumbnailSize", Experimental),
        Card("SC_SquareCorners", Experimental),
    ];

    private static SettingsSearchEntry Card(string uid, int tab) => new(uid, tab, IsCard: true);

    private static SettingsSearchEntry Heading(string uid, int tab) => new(uid, tab, IsCard: false);
}

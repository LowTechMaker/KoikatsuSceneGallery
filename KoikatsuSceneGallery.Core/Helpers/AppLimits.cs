namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How much the app does at once and how far it goes: worker counts, batch
/// sizes, caps on lists, and the thresholds that decide what counts as
/// similar. One place for them (user request), as for timing (AppTiming),
/// motion, sizes and colours.
/// </summary>
/// <remarks>
/// Limits that belong to one rule and are documented with it stay with their
/// owner, and are listed here so this stays the index:
/// <see cref="ArtworkRefetchPolicy"/> (pacing, failures), <see cref="HomeThemes"/>
/// (theme sizes, "dusty" and "never played" rules), <see cref="PokerHand"/>
/// (hand size and joker odds), <c>ArchiveExtractor</c> (entry and size caps),
/// <c>MetadataValueReader</c> (parse caps), and each store's schema version.
/// </remarks>
internal static class AppLimits
{
    // ------------------------------------------------------------ parallel work

    /// <summary>
    /// Workers for disk-bound work (scanning folders, parsing metadata, copying
    /// on import, installing mods): one fewer than the processors, at most 4.
    /// It was written five ways, and the import had no cap at all.
    /// </summary>
    public static int IoWorkers => Math.Clamp(Environment.ProcessorCount - 1, 1, 4);
    /// <summary>Thumbnail decoding: a small pool and a bounded queue, so the UI stays first.</summary>
    public const int ThumbnailWorkers = 2;
    public const int ThumbnailQueue = 128;

    // ------------------------------------------------------------ batches

    /// <summary>Cards sent to the UI per batch while a folder is scanned.</summary>
    public const int ScanBatchSize = 200;
    /// <summary>Cached cards put on screen first, before the rest, at start.</summary>
    public const int FirstScreenCards = 200;

    // ------------------------------------------------------------ lists

    /// <summary>Cards on a home shelf (and in one of its themes).</summary>
    public const int ShelfSize = 30;
    /// <summary>Cards offered when picking a local source's cover.</summary>
    public const int SourceCoverCandidates = 240;
    /// <summary>Random cards kept ready for the shuffle sort.</summary>
    public const int ShufflePool = 20;
    /// <summary>The gallery's container cache length (how many screens ahead and behind are realised).</summary>
    public const int DefaultCacheLength = 4;

    // ------------------------------------------------------------ files

    /// <summary>Attempts at a free name ("x (2).png", "x (3).png", …) before giving up.</summary>
    public const int RenameAttempts = 1000;

    // ------------------------------------------------------------ similarity (perceptual hash distance: lower = more alike)

    /// <summary>Grouping cards as versions of one another: alike at or under this distance.</summary>
    public const int GroupingHashDistance = 22;
    /// <summary>The home page's "looks alike" theme: stricter, it shows them side by side.</summary>
    public const int SimilarThemeHashDistance = 10;
}

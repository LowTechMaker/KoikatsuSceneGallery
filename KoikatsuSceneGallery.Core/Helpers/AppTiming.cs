namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How long the app waits for things that are not animations (those are
/// CardMotion's): debounces, refresh intervals, timeouts and how long a notice
/// stays up. One place for them, as for motion, sizes and colours (user
/// request); each used to be a literal where it was used, and the same idea
/// had four different values (the search box debounce).
/// </summary>
internal static class AppTiming
{
    // ------------------------------------------------------------ typing and refreshing

    /// <summary>A search box: wait this long after the last key before filtering. Library, authors, discovery.</summary>
    public static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);
    /// <summary>The command palette: quicker than a search box, it should feel instant.</summary>
    public static readonly TimeSpan PaletteDebounce = TimeSpan.FromMilliseconds(120);
    /// <summary>Coalesces a burst of library changes into one refresh of a grid.</summary>
    public static readonly TimeSpan ViewRefreshDebounce = TimeSpan.FromMilliseconds(80);
    /// <summary>Random discovery's refresh after a change; slower while the library is still loading.</summary>
    public static readonly TimeSpan DiscoveryRefresh = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DiscoveryRefreshWhileLoading = TimeSpan.FromMilliseconds(400);
    /// <summary>Rebuilding a list of tiles (authors, local sources) after their data changed.</summary>
    public static readonly TimeSpan RebuildDebounce = TimeSpan.FromMilliseconds(500);
    /// <summary>Re-reading the metadata filter's options while cards are being parsed.</summary>
    public static readonly TimeSpan MetadataFilterRefresh = TimeSpan.FromMilliseconds(750);
    /// <summary>A file watcher's burst of events settling before it is applied.</summary>
    public static readonly TimeSpan FileWatchSettle = TimeSpan.FromMilliseconds(300);
    /// <summary>The import page: matching typed links, and resolving again after a change.</summary>
    public static readonly TimeSpan ImportLinkMatchDebounce = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan ImportReResolveDelay = TimeSpan.FromMilliseconds(150);
    /// <summary>How long the import page waits for a fetch before showing the cards anyway.</summary>
    public static readonly TimeSpan ImportFetchWait = TimeSpan.FromSeconds(3);
    /// <summary>Checking whether the login window has the cookie yet.</summary>
    public static readonly TimeSpan CookiePoll = TimeSpan.FromMilliseconds(500);

    // ------------------------------------------------------------ waiting on work

    /// <summary>On close, how long to wait for the metadata parsers to stop.</summary>
    public static readonly TimeSpan ShutdownWait = TimeSpan.FromSeconds(5);
    /// <summary>How long a scan batch waits for the UI thread (raised from 10 s: at start it is busy showing the scene cache).</summary>
    public static readonly TimeSpan ScanBatchUiWait = TimeSpan.FromSeconds(30);
    /// <summary>Checking a plugin for updates.</summary>
    public static readonly TimeSpan PluginUpdateTimeout = TimeSpan.FromSeconds(10);
    /// <summary>Resolving one post link.</summary>
    public static readonly TimeSpan PostLinkTimeout = TimeSpan.FromSeconds(20);
    /// <summary>A resolution slower than this is logged as slow.</summary>
    public static readonly TimeSpan SlowResolution = TimeSpan.FromMilliseconds(750);

    // ------------------------------------------------------------ notices

    /// <summary>A toast that says something happened.</summary>
    public static readonly TimeSpan ToastShort = TimeSpan.FromSeconds(4);
    /// <summary>A toast about a problem: longer, it may need reading.</summary>
    public static readonly TimeSpan ToastLong = TimeSpan.FromSeconds(8);
    /// <summary>The loupe's line of key hints, as long as a short toast.</summary>
    public static readonly TimeSpan HintDuration = ToastShort;
    /// <summary>The home page's featured card turns to the next one this often (not while the pointer is on it).</summary>
    public static readonly TimeSpan FeaturedCycle = TimeSpan.FromSeconds(7);
    /// <summary>A setting found by the settings search stays highlighted this long.</summary>
    public static readonly TimeSpan SettingsHighlight = TimeSpan.FromSeconds(3);
    /// <summary>An author tile turns to its next picture about this often (plus up to <see cref="LiveTileJitter"/>).</summary>
    public static readonly TimeSpan LiveTileCycle = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan LiveTileJitter = TimeSpan.FromSeconds(4);
}

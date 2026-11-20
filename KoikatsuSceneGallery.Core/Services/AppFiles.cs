namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The names of every file and folder the app writes: in its own data folder
/// (settings, caches, logs, backups) and inside the library (the
/// <c>.scenegallery</c> folder and its files). One list of them (user request),
/// so a name is never spelt twice and a new one cannot quietly clash with an
/// old one (a unit test checks they are distinct).
/// </summary>
/// <remarks>Each store keeps its own schema version; the names live here.</remarks>
internal static class AppFiles
{
    // ------------------------------------------------------------ the app's data folder (AppPaths.LocalFolder)

    /// <summary>The data folder's own name, when it is not the packaged app's.</summary>
    public const string DataFolderName = "KoikatsuSceneGallery";
    /// <summary>Installed plugins, next to the app and in the data folder.</summary>
    public const string PluginsFolder = "Plugins";
    public const string ThumbnailCacheFolder = "gallery_temp";
    public const string ImportStagingFolder = "import-staging";
    public const string ImportOverwrittenFolder = "import-overwritten";
    public const string RefetchBackupsFolder = "refetch-backups";
    public const string ModBackupsFolder = "mod-backups";

    public const string Settings = "config.json";
    public const string SceneCardCache = "scene_cards.json";
    public const string ScenePostsSnapshot = "scene_posts.json";
    public const string SceneMetadataCache = "scene_metadata.json";
    public const string CharacterMetadataCache = "chara_metadata.v2.json";
    public const string CoordinateMetadataCache = "coord_metadata.v2.json";
    public const string ArtworkRefetchState = "artwork-refetch.json";
    public const string ArtworkRefetchGone = "artwork-refetch-gone.json";

    public const string CrashLog = "crash.log";
    public const string PluginsLog = "plugins.log";
    public const string StartupTimingLog = "startup_timing.log";
    public const string UsageLog = "usage_log.jsonl";
    public const string ModInstallLog = "mod-installs.jsonl";

    // ------------------------------------------------------------ inside the library

    /// <summary>The app's folder inside a library root (or an author or artwork folder).</summary>
    public const string LibraryMetadataFolder = ".scenegallery";
    /// <summary>Post sidecars, inside <see cref="LibraryMetadataFolder"/>.</summary>
    public const string PostSidecarFolder = "fetched_data";
    public const string CardStates = "card_states.json";
    public const string UserRatings = "user_ratings.json";
    public const string CharacterAnnotations = "characters.json";
    public const string LocalSource = "source.json";

    // ------------------------------------------------------------ naming helpers

    /// <summary>The timestamp in backup folder names (import overwrites, mod backups).</summary>
    public const string BackupTimestampFormat = "yyyyMMdd-HHmmss";

    /// <summary>Every name above, for the uniqueness check.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        DataFolderName, PluginsFolder, ThumbnailCacheFolder, ImportStagingFolder, ImportOverwrittenFolder,
        RefetchBackupsFolder, ModBackupsFolder, Settings, SceneCardCache, ScenePostsSnapshot, SceneMetadataCache,
        CharacterMetadataCache, CoordinateMetadataCache, ArtworkRefetchState, ArtworkRefetchGone, CrashLog,
        PluginsLog, StartupTimingLog, UsageLog, ModInstallLog, LibraryMetadataFolder, PostSidecarFolder,
        CardStates, UserRatings, CharacterAnnotations, LocalSource,
    ];
}

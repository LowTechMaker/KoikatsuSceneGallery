using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

public class SettingsService
{
    private const string ConfigFileName = "config.json";
    private readonly string _configPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        _configPath = Path.Combine(AppPaths.LocalFolder, ConfigFileName);
    }

    public async Task<ConfigData> LoadConfigAsync()
    {
        if (!File.Exists(_configPath))
            return new ConfigData();

        try
        {
            await using var stream = File.OpenRead(_configPath);
            return await JsonSerializer.DeserializeAsync<ConfigData>(stream) ?? new ConfigData();
        }
        catch (Exception)
        {
            // A corrupt or unreadable config must never take the app down — fall
            // back to defaults rather than throwing into callers (several of
            // which run on the UI thread during startup/navigation).
            return new ConfigData();
        }
    }

    public async Task SaveConfigAsync(ConfigData config)
    {
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    public async Task<List<string>> LoadFolderPathsAsync()
    {
        var config = await LoadConfigAsync();
        return config.FolderPaths;
    }

    public async Task SaveFolderPathsAsync(IEnumerable<string> folderPaths)
    {
        var config = await LoadConfigAsync();
        config.FolderPaths = folderPaths.ToList();
        await SaveConfigAsync(config);
    }

    public class ConfigData
    {
        public List<string> FolderPaths { get; set; } = [];

        /// <summary>Folders scanned for Koikatsu character cards (.png), tracked
        /// separately from scene folders.</summary>
        public List<string> CharacterFolderPaths { get; set; } = [];
        public bool ResolutionFilterEnabled { get; set; } = true;
        public List<string> AllowedResolutions { get; set; } = ["320x180", "1600x900"];
        public bool ShowFileNames { get; set; } = true;
        public bool ScrollToTopOnSort { get; set; } = true;

        /// <summary>
        /// Gallery thumbnail card width in pixels. Adjusted via Ctrl+mouse wheel
        /// in the gallery. Column count then reflows with the window width.
        /// </summary>
        public double ThumbnailWidth { get; set; } = 240;

        /// <summary>
        /// Whether the gallery shows the small/medium/large size buttons. Off =
        /// buttons hidden and thumbnails fixed at the medium size. Off by default.
        /// </summary>
        public bool SizeSelectorEnabled { get; set; } = false;

        /// <summary>
        /// Off-screen render buffer for the gallery, as a multiple of the
        /// viewport (ItemsWrapGrid.CacheLength). Higher = more thumbnails kept
        /// realized above/below the view (smoother fast-scroll) but more eager
        /// thumbnail generation and memory/CPU. Advanced setting; 4 is the WinUI
        /// default.
        /// </summary>
        public double CacheLength { get; set; } = 4;

        /// <summary>
        /// Whether to parse embedded plugin GUIDs to classify scenes (environment
        /// / Timeline) and expose the metadata filters. Off = no scanning, no
        /// filter UI — for users who don't want the feature. Off by default;
        /// users opt in from Settings.
        /// </summary>
        public bool PluginAnalysisEnabled { get; set; } = false;

        public string CacheFolderPath { get; set; } = "";

        /// <summary>API key used for SauceNao reverse image search during import.</summary>
        public string SauceNaoApiKey { get; set; } = "";

        /// <summary>
        /// UI language override. Empty = follow the system language.
        /// Otherwise a BCP-47 tag: "en-US", "zh-Hans", "zh-Hant".
        /// </summary>
        public string Language { get; set; } = "";

        public bool CharacterResolutionFilterEnabled { get; set; } = false;
        public List<string> CharacterAllowedResolutions { get; set; } = ["252x352"];

        public List<string> CoordinateFolderPaths { get; set; } = [];
        public bool CoordinateResolutionFilterEnabled { get; set; } = false;
        public List<string> CoordinateAllowedResolutions { get; set; } = ["252x352"];

        /// <summary>
        /// Relative subfolder path inserted between the library root and the author
        /// folder when resolving import destinations. Can be multi-level (e.g. "整理\pixiv").
        /// Empty = place directly under the root (legacy behaviour).
        /// </summary>
        public string ImportSubfolder { get; set; } = "Organized";

        /// <summary>
        /// When the number of files from the same pixiv artwork in one import batch
        /// is strictly greater than this value, a subfolder named after the artwork
        /// title is created inside the author folder. 0 = always create; -1 = never.
        /// </summary>
        public int ArtworkSubfolderThreshold { get; set; } = 1;

        public bool UseVisualSimilarity { get; set; }

        // ── Folder naming (OCD) ─────────────────────────────────────
        public string AuthorFolderFormat { get; set; } = "{name} ({id})";
        public string ArtworkFolderFormat { get; set; } = "{title} ({id})";
        public string UnknownFolderName { get; set; } = "Unknown";
        public string KoikatsuFolderName { get; set; } = "Koikatsu";
        public string KoikatsuSunshineFolderName { get; set; } = "KoikatsuSunshine";
        public string GFolderName { get; set; } = "G";
        public string R18FolderName { get; set; } = "R-18";
        public string R18GFolderName { get; set; } = "R-18G";
    }
}

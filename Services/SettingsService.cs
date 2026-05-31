using System.Text.Json;
using Windows.Storage;

namespace KoikatsuSceneGallery.Services;

public class SettingsService
{
    private const string ConfigFileName = "config.json";
    private readonly string _configPath;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public SettingsService()
    {
        _configPath = Path.Combine(ApplicationData.Current.LocalFolder.Path, ConfigFileName);
    }

    public async Task<ConfigData> LoadConfigAsync()
    {
        if (!File.Exists(_configPath))
            return new ConfigData();

        await using var stream = File.OpenRead(_configPath);
        return await JsonSerializer.DeserializeAsync<ConfigData>(stream) ?? new ConfigData();
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

        /// <summary>
        /// UI language override. Empty = follow the system language.
        /// Otherwise a BCP-47 tag: "en-US", "zh-Hans", "zh-Hant".
        /// </summary>
        public string Language { get; set; } = "";
    }
}

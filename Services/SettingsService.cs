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
        public bool ResolutionFilterEnabled { get; set; } = true;
        public List<string> AllowedResolutions { get; set; } = ["320x180", "1600x900"];
        public bool ShowFileNames { get; set; } = true;
        public bool ScrollToTopOnSort { get; set; } = true;

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

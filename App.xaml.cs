using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;

namespace KoikatsuSceneGallery;

public partial class App : Application
{
    private static MainWindow? _mainWindow;

    public static MainWindow MainWindow => _mainWindow!;
    public static SettingsService SettingsService { get; } = new();
    public static SceneCardService SceneCardService { get; } = new();
    public static ThumbnailCacheService ThumbnailCacheService { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;
    public static GalleryViewModel GalleryViewModel { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var config = await SettingsService.LoadConfigAsync();

        // Apply the saved UI language override before any window/page is created.
        // Empty means follow the system language (resources fall back to en-US).
        if (!string.IsNullOrEmpty(config.Language))
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = config.Language;

        ThumbnailCacheService = new ThumbnailCacheService(config.CacheFolderPath);

        SettingsViewModel = new SettingsViewModel(SettingsService);
        GalleryViewModel = new GalleryViewModel(SceneCardService, SettingsService, ThumbnailCacheService);

        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}

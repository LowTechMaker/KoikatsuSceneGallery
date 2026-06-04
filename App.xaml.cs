using KoikatsuSceneGallery.Helpers;
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
    public static CharacterCardService CharacterCardService { get; } = new();
    public static ThumbnailCacheService ThumbnailCacheService { get; private set; } = null!;
    public static SceneMetadataService SceneMetadataService { get; } = new();
    public static CharacterMetadataService CharacterMetadataService { get; } = new();
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;
    public static GalleryViewModel GalleryViewModel { get; private set; } = null!;
    public static CharacterGalleryViewModel CharacterGalleryViewModel { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // No debugger is attached in a packaged-zip test build, so route every
        // flavour of unhandled exception to a crash log the tester can hand back.
        // UI-thread exceptions are also marked handled so a single bad operation
        // (e.g. decoding one corrupt scene) doesn't take down the whole window.
        UnhandledException += (_, e) =>
        {
            CrashLog.Write("UI", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("Domain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Task", e.Exception);
            e.SetObserved();
        };
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            var config = await SettingsService.LoadConfigAsync();

            // Apply the saved UI language override before any window/page is created.
            // Empty means follow the system language (resources fall back to en-US).
            if (!string.IsNullOrEmpty(config.Language))
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = config.Language;

            ThumbnailCacheService = new ThumbnailCacheService(config.CacheFolderPath);
        }
        catch (Exception ex)
        {
            // A bad config or cache path must not stop the window from opening,
            // otherwise the app can become permanently unlaunchable.
            CrashLog.Write("OnLaunched", ex);
            ThumbnailCacheService ??= new ThumbnailCacheService(null);
        }

        SettingsViewModel = new SettingsViewModel(SettingsService);
        GalleryViewModel = new GalleryViewModel(SceneCardService, SettingsService, ThumbnailCacheService, SceneMetadataService);
        CharacterGalleryViewModel = new CharacterGalleryViewModel(CharacterCardService, SettingsService, ThumbnailCacheService, CharacterMetadataService);

        _mainWindow = new MainWindow();
        _mainWindow.Activate();
    }
}

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
    public static CoordinateCardService CoordinateCardService { get; } = new();
    public static ThumbnailCacheService ThumbnailCacheService { get; private set; } = null!;
    public static SceneMetadataService SceneMetadataService { get; } = new();
    public static CharacterMetadataService CharacterMetadataService { get; } = new();
    public static CoordinateMetadataService CoordinateMetadataService { get; } = new();
    public static PluginService PluginService { get; } = new();
    public static AuthorInfoService AuthorInfoService { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;
    public static GalleryViewModel GalleryViewModel { get; private set; } = null!;
    public static CharacterGalleryViewModel CharacterGalleryViewModel { get; private set; } = null!;
    public static CoordinateGalleryViewModel CoordinateGalleryViewModel { get; private set; } = null!;
    public static AuthorsViewModel AuthorsViewModel { get; private set; } = null!;
    public static ImportService? ImportService { get; private set; }
    public static ImportViewModel? ImportViewModel { get; private set; }
    public static AuthorPostService? AuthorPostService { get; private set; }

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
        SettingsService.ConfigData? config = null;
        try
        {
            config = await SettingsService.LoadConfigAsync();

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

        try
        {
            // Local-disk reflection only; a broken plugin is recorded as Failed
            // and must never stop the window from opening.
            PluginService.LoadPlugins();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Plugins", ex);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var updates = await new PluginUpdateChecker()
                    .CheckUpdatesAsync(PluginService.Plugins, CancellationToken.None);
                if (updates.Count > 0)
                    PluginService.ApplyUpdateInfo(updates);
            }
            catch (Exception ex)
            {
                CrashLog.Write("PluginUpdateCheck", ex);
            }
        });

        AuthorInfoService = new AuthorInfoService(
            PluginService.AuthorProvider,
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        SettingsViewModel = new SettingsViewModel(SettingsService);
        GalleryViewModel = new GalleryViewModel(SceneCardService, SettingsService, ThumbnailCacheService, SceneMetadataService);
        CharacterGalleryViewModel = new CharacterGalleryViewModel(CharacterCardService, SettingsService, ThumbnailCacheService, CharacterMetadataService);
        CoordinateGalleryViewModel = new CoordinateGalleryViewModel(CoordinateCardService, SettingsService, ThumbnailCacheService, CoordinateMetadataService);

        if (AuthorInfoService.IsAvailable)
        {
            if (config is not null)
                AuthorInfoService.UpdateRoots(
                    [.. config.FolderPaths, .. config.CharacterFolderPaths, .. config.CoordinateFolderPaths]);
            AuthorInfoService.Attach(GalleryViewModel.Cards, AuthorCardKind.Scene);
            AuthorInfoService.Attach(CharacterGalleryViewModel.Cards, AuthorCardKind.Character);
            AuthorInfoService.Attach(CoordinateGalleryViewModel.Cards, AuthorCardKind.Coordinate);

            // Folder-list edits change which directories count as library roots;
            // refresh them so author resolution follows (galleries reload too).
            SettingsViewModel.SceneFolderPathsChanged += OnAnyFolderPathsChanged;
            SettingsViewModel.CharacterFolderPathsChanged += OnAnyFolderPathsChanged;
            SettingsViewModel.CoordinateFolderPathsChanged += OnAnyFolderPathsChanged;
        }

        if (PluginService.ImportProviders.Count > 0)
        {
            ImportService = new ImportService(
                PluginService.ImportProviders,
                PluginService.AuthorProvider,
                PluginService.ReverseImageSearchProvider,
                SettingsService);
            ImportViewModel = new ImportViewModel(
                ImportService,
                SettingsService,
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

            if (PluginService.AuthorProvider is not null)
            {
                AuthorPostService = new AuthorPostService(
                    PluginService.ImportProviders,
                    PluginService.AuthorProvider,
                    SettingsService);
            }
        }

        AuthorsViewModel = new AuthorsViewModel(
            AuthorInfoService,
            Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => PluginService.Shutdown();
        _mainWindow.Activate();
    }

    private static void OnAnyFolderPathsChanged()
    {
        var vm = SettingsViewModel;
        AuthorInfoService.UpdateRoots(
            [.. vm.FolderPaths, .. vm.CharacterFolderPaths, .. vm.CoordinateFolderPaths]);
    }
}

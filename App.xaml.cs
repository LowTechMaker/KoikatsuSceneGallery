using System.Diagnostics;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery;

public partial class App : Application
{
    public static AppServiceRegistry Services { get; } = new();

    private readonly IAppLogger _logger = new CrashLogLogger();
    private readonly SettingsService _settingsService = new();
    private readonly SceneCardService _sceneCardService = new();
    private readonly CharacterCardService _characterCardService = new();
    private readonly CoordinateCardService _coordinateCardService = new();
    private readonly SceneCardCacheService _sceneCardCacheService;
    private readonly SceneMetadataService _sceneMetadataService;
    private readonly CharacterMetadataService _characterMetadataService;
    private readonly CoordinateMetadataService _coordinateMetadataService;
    private readonly MediaCardService _screenshotCardService = new([".png", ".jpg", ".jpeg", ".bmp"], isVideo: false);
    private readonly MediaCardService _videoCardService = new([".mp4", ".avi", ".webm", ".mov", ".mkv", ".gif"], isVideo: true);
    private readonly PluginService _pluginService;
    private ThumbnailCacheService _thumbnailCacheService = null!;
    private MainWindow? _mainWindow;

    public App()
    {
        _sceneCardCacheService = new SceneCardCacheService(_logger);
        _sceneMetadataService = new SceneMetadataService(_logger);
        _characterMetadataService = new CharacterMetadataService(_logger);
        _coordinateMetadataService = new CoordinateMetadataService(_logger);
        _pluginService = new PluginService(
            _logger,
            Path.Combine(AppPaths.LocalFolder, "Plugins"));
        InitializeComponent();

        // No debugger is attached in a packaged-zip test build, so route every
        // flavour of unhandled exception to a crash log the tester can hand back.
        // UI-thread exceptions are also marked handled so a single bad operation
        // (e.g. decoding one corrupt scene) doesn't take down the whole window.
        UnhandledException += (_, e) =>
        {
            _logger.LogError("UI", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            _logger.LogError("Domain", e.ExceptionObject as Exception ?? new InvalidOperationException("Unknown unhandled exception"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _logger.LogError("Task", e.Exception);
            e.SetObserved();
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
        => UiEventGuard.Run(_logger, "App.OnLaunched", () => OnLaunchedAsync(args));

    private async Task OnLaunchedAsync(LaunchActivatedEventArgs args)
    {
        SettingsService.ConfigData? config = null;
        try
        {
            config = await _settingsService.LoadConfigAsync();

            // Apply the saved UI language override before any window/page is created.
            // Empty means follow the system language (resources fall back to en-US).
            if (!string.IsNullOrEmpty(config.Language))
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = config.Language;

            _thumbnailCacheService = new ThumbnailCacheService(_logger, config.CacheFolderPath);
        }
        catch (Exception ex)
        {
            // A bad config or cache path must not stop the window from opening,
            // otherwise the app can become permanently unlaunchable.
            _logger.LogError("OnLaunched", ex);
            _thumbnailCacheService ??= new ThumbnailCacheService(_logger, null);
        }

        try
        {
            // Local-disk reflection only; a broken plugin is recorded as Failed
            // and must never stop the window from opening.
            _pluginService.LoadPlugins();
        }
        catch (Exception ex)
        {
            _logger.LogError("Plugins", ex);
        }

        Task.Run(async () =>
        {
            try
            {
                var updates = await new PluginUpdateChecker(_logger)
                    .CheckUpdatesAsync(
                        _pluginService.Plugins,
                        _pluginService.UpdateProviders,
                        CancellationToken.None);
                if (updates.Count > 0)
                    _pluginService.ApplyUpdateInfo(updates);
            }
            catch (Exception ex)
            {
                _logger.LogError("PluginUpdateCheck", ex);
            }
        }).Observe(_logger, "PluginUpdate.BackgroundCheck");

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var authorInfoService = new AuthorInfoService(
            _pluginService.AuthorProviders,
            dispatcherQueue,
            _logger);

        GalleryViewModel? galleryViewModel = null;
        var settingsViewModel = new SettingsViewModel(
            _settingsService,
            _thumbnailCacheService,
            () => _mainWindow,
            () => galleryViewModel,
            _logger);
        settingsViewModel.Load(config ?? new SettingsService.ConfigData());
        galleryViewModel = new GalleryViewModel(
            _sceneCardService,
            _settingsService,
            _thumbnailCacheService,
            _sceneMetadataService,
            _sceneCardCacheService,
            settingsViewModel,
            _pluginService,
            _logger);
        var characterGalleryViewModel = new CharacterGalleryViewModel(
            _characterCardService,
            _settingsService,
            _thumbnailCacheService,
            _characterMetadataService,
            settingsViewModel,
            _logger);
        var coordinateGalleryViewModel = new CoordinateGalleryViewModel(
            _coordinateCardService,
            _settingsService,
            _thumbnailCacheService,
            _coordinateMetadataService,
            settingsViewModel,
            _logger);
        var screenshotGalleryViewModel = new MediaGalleryViewModel(
            _screenshotCardService,
            _settingsService,
            _thumbnailCacheService,
            settingsViewModel,
            _logger,
            isVideo: false);
        var videoGalleryViewModel = new MediaGalleryViewModel(
            _videoCardService,
            _settingsService,
            _thumbnailCacheService,
            settingsViewModel,
            _logger,
            isVideo: true);

        ImportService? importService = null;
        ImportViewModel? importViewModel = null;
        AuthorPostService? authorPostService = null;
        if (_pluginService.ImportProviders.Count > 0)
        {
            importService = new ImportService(
                _pluginService.ImportProviders,
                _pluginService.AuthorProviders,
                _pluginService.ReverseImageSearchProvider,
                _settingsService,
                _logger);
            importViewModel = new ImportViewModel(
                importService,
                _settingsService,
                _pluginService,
                dispatcherQueue,
                _logger);

            if (_pluginService.AuthorProviders.Count > 0)
            {
                authorPostService = new AuthorPostService(
                    _pluginService.ImportProviders,
                    _pluginService.AuthorProviders,
                    _settingsService,
                    _logger);
            }
        }

        var authorsViewModel = new AuthorsViewModel(
            authorInfoService,
            dispatcherQueue,
            settingsViewModel,
            _thumbnailCacheService,
            galleryViewModel);
        var authorSourceCoordinator = new AuthorSourceCoordinator(
            authorInfoService,
            settingsViewModel,
            galleryViewModel,
            characterGalleryViewModel,
            coordinateGalleryViewModel);
        authorSourceCoordinator.Initialize();

        RegisterServices(
            authorInfoService,
            settingsViewModel,
            galleryViewModel,
            characterGalleryViewModel,
            coordinateGalleryViewModel,
            screenshotGalleryViewModel,
            videoGalleryViewModel,
            authorsViewModel,
            authorSourceCoordinator,
            importService,
            importViewModel,
            authorPostService);

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => _pluginService.Shutdown();
        _mainWindow.Activate();

        PluginService.InputRequestHandler = (title, message, placeholder, ct) =>
            ShowInputDialogAsync(dispatcherQueue, title, message, placeholder, ct);

        authorSourceCoordinator.EnsureLoadedAsync().Observe(_logger, "AuthorSource.InitialLoad");
    }

    private void RegisterServices(
        AuthorInfoService authorInfoService,
        SettingsViewModel settingsViewModel,
        GalleryViewModel galleryViewModel,
        CharacterGalleryViewModel characterGalleryViewModel,
        CoordinateGalleryViewModel coordinateGalleryViewModel,
        MediaGalleryViewModel screenshotGalleryViewModel,
        MediaGalleryViewModel videoGalleryViewModel,
        AuthorsViewModel authorsViewModel,
        AuthorSourceCoordinator authorSourceCoordinator,
        ImportService? importService,
        ImportViewModel? importViewModel,
        AuthorPostService? authorPostService)
    {
        Services.Add(_logger);
        Services.Add(_settingsService);
        Services.Add(_sceneCardService);
        Services.Add(_characterCardService);
        Services.Add(_coordinateCardService);
        Services.Add(_thumbnailCacheService);
        Services.Add(_sceneCardCacheService);
        Services.Add(_sceneMetadataService);
        Services.Add(_characterMetadataService);
        Services.Add(_coordinateMetadataService);
        Services.Add(_screenshotCardService, "screenshots");
        Services.Add(_videoCardService, "videos");
        Services.Add(_pluginService);
        Services.Add(authorInfoService);
        Services.Add(settingsViewModel);
        Services.Add(galleryViewModel);
        Services.Add(characterGalleryViewModel);
        Services.Add(coordinateGalleryViewModel);
        Services.Add(screenshotGalleryViewModel, "screenshots");
        Services.Add(videoGalleryViewModel, "videos");
        Services.Add(authorsViewModel);
        Services.Add(authorSourceCoordinator);
        if (importService is not null) Services.Add(importService);
        if (importViewModel is not null) Services.Add(importViewModel);
        if (authorPostService is not null) Services.Add(authorPostService);
    }

    private static readonly ResourceLoader ResLoader = new();

    private Task<string?> ShowInputDialogAsync(
        DispatcherQueue dispatcherQueue,
        string title,
        string message,
        string? placeholder,
        CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<string?>();
        var reg = ct.Register(() => tcs.TrySetResult(null));

        if (!dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var xamlRoot = _mainWindow?.Content?.XamlRoot;
                if (xamlRoot is null)
                {
                    tcs.TrySetResult(null);
                    return;
                }

                var textBox = new TextBox
                {
                    PlaceholderText = placeholder ?? "",
                    MinWidth = 360,
                };

                var dialog = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = title,
                    Content = new StackPanel
                    {
                        Spacing = 12,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = message,
                                TextWrapping = TextWrapping.Wrap,
                            },
                            textBox,
                        },
                    },
                    PrimaryButtonText = ResLoader.GetString("Common_OK"),
                    CloseButtonText = ResLoader.GetString("Common_Cancel"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                var result = await dialog.ShowAsync();
                var value = result == ContentDialogResult.Primary
                    ? textBox.Text?.Trim()
                    : null;
                tcs.TrySetResult(string.IsNullOrWhiteSpace(value) ? null : value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ShowInputDialogAsync failed: {ex.Message}");
                tcs.TrySetResult(null);
            }
            finally
            {
                await reg.DisposeAsync();
            }
        }))
        {
            reg.Dispose();
            tcs.TrySetResult(null);
        }

        return tcs.Task;
    }

}

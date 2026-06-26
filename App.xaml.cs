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
    private static MainWindow? _mainWindow;

    public static MainWindow MainWindow => _mainWindow!;
    public static SettingsService SettingsService { get; } = new();
    public static SceneCardService SceneCardService { get; } = new();
    public static CharacterCardService CharacterCardService { get; } = new();
    public static CoordinateCardService CoordinateCardService { get; } = new();
    public static ThumbnailCacheService ThumbnailCacheService { get; private set; } = null!;
    public static SceneCardCacheService SceneCardCacheService { get; } = new();
    public static SceneMetadataService SceneMetadataService { get; } = new();
    public static CharacterMetadataService CharacterMetadataService { get; } = new();
    public static CoordinateMetadataService CoordinateMetadataService { get; } = new();
    public static MediaCardService ScreenshotCardService { get; } = new([".png", ".jpg", ".jpeg", ".bmp"], isVideo: false);
    public static MediaCardService VideoCardService { get; } = new([".mp4", ".avi", ".webm", ".mov", ".mkv", ".gif"], isVideo: true);
    public static PluginService PluginService { get; } = new();
    public static AuthorInfoService AuthorInfoService { get; private set; } = null!;
    public static SettingsViewModel SettingsViewModel { get; private set; } = null!;
    public static GalleryViewModel GalleryViewModel { get; private set; } = null!;
    public static CharacterGalleryViewModel CharacterGalleryViewModel { get; private set; } = null!;
    public static CoordinateGalleryViewModel CoordinateGalleryViewModel { get; private set; } = null!;
    public static AuthorsViewModel AuthorsViewModel { get; private set; } = null!;
    public static MediaGalleryViewModel ScreenshotGalleryViewModel { get; private set; } = null!;
    public static MediaGalleryViewModel VideoGalleryViewModel { get; private set; } = null!;
    public static ImportService? ImportService { get; private set; }
    public static ImportViewModel? ImportViewModel { get; private set; }
    public static AuthorPostService? AuthorPostService { get; private set; }
    private static Task? _authorSourcesWarmupTask;

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
                    .CheckUpdatesAsync(
                        PluginService.Plugins,
                        PluginService.UpdateProviders,
                        CancellationToken.None);
                if (updates.Count > 0)
                    PluginService.ApplyUpdateInfo(updates);
            }
            catch (Exception ex)
            {
                CrashLog.Write("PluginUpdateCheck", ex);
            }
        });

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        AuthorInfoService = new AuthorInfoService(
            PluginService.AuthorProviders,
            dispatcherQueue);

        SettingsViewModel = new SettingsViewModel(SettingsService);
        SettingsViewModel.Load(config ?? new SettingsService.ConfigData());
        GalleryViewModel = new GalleryViewModel(SceneCardService, SettingsService, ThumbnailCacheService, SceneMetadataService, SceneCardCacheService);
        CharacterGalleryViewModel = new CharacterGalleryViewModel(CharacterCardService, SettingsService, ThumbnailCacheService, CharacterMetadataService);
        CoordinateGalleryViewModel = new CoordinateGalleryViewModel(CoordinateCardService, SettingsService, ThumbnailCacheService, CoordinateMetadataService);
        ScreenshotGalleryViewModel = new MediaGalleryViewModel(ScreenshotCardService, SettingsService, ThumbnailCacheService, isVideo: false);
        VideoGalleryViewModel = new MediaGalleryViewModel(VideoCardService, SettingsService, ThumbnailCacheService, isVideo: true);

        if (AuthorInfoService.IsAvailable)
        {
            ApplyAuthorLibraryRoots();
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
                PluginService.AuthorProviders,
                PluginService.ReverseImageSearchProvider,
                SettingsService);
            ImportViewModel = new ImportViewModel(
                ImportService,
                SettingsService,
                dispatcherQueue);

            if (PluginService.AuthorProviders.Count > 0)
            {
                AuthorPostService = new AuthorPostService(
                    PluginService.ImportProviders,
                    PluginService.AuthorProviders,
                    SettingsService);
            }
        }

        AuthorsViewModel = new AuthorsViewModel(
            AuthorInfoService,
            dispatcherQueue);

        _mainWindow = new MainWindow();
        _mainWindow.Closed += (_, _) => PluginService.Shutdown();
        _mainWindow.Activate();

        PluginService.InputRequestHandler = (title, message, placeholder, ct) =>
            ShowInputDialogAsync(dispatcherQueue, title, message, placeholder, ct);

        _ = EnsureAuthorSourcesLoadedAsync();
    }

    private static readonly ResourceLoader ResLoader = new();

    private static Task<string?> ShowInputDialogAsync(
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

    private static void OnAnyFolderPathsChanged()
    {
        RefreshAuthorSources(reloadLoadedSources: true);
    }

    public static void RefreshAuthorSources(bool reloadLoadedSources = false)
    {
        if (!AuthorInfoService.IsAvailable)
            return;

        ApplyAuthorLibraryRoots();
        AuthorInfoService.RebuildAssignments(
            GalleryViewModel.Cards,
            CharacterGalleryViewModel.Cards,
            CoordinateGalleryViewModel.Cards);
        _authorSourcesWarmupTask = LoadAuthorSourcesAsync(reloadLoadedSources);
    }

    private static void ApplyAuthorLibraryRoots()
    {
        var vm = SettingsViewModel;
        AuthorInfoService.UpdateRoots(
            [.. vm.FolderPaths, .. vm.CharacterFolderPaths, .. vm.CoordinateFolderPaths]);
    }

    public static Task EnsureAuthorSourcesLoadedAsync()
    {
        if (!AuthorInfoService.IsAvailable)
            return Task.CompletedTask;

        if (_authorSourcesWarmupTask is { IsCompleted: false } runningTask)
            return runningTask;

        _authorSourcesWarmupTask = LoadAuthorSourcesAsync();
        return _authorSourcesWarmupTask;
    }

    private static async Task LoadAuthorSourcesAsync(bool forceReload = false)
    {
        var loadTasks = new List<Task>(3);

        AwaitOrLoad(loadTasks, GalleryViewModel.Cards.Count, GalleryViewModel.IsLoading, GalleryViewModel.LoadCardsCommand, forceReload);
        AwaitOrLoad(loadTasks, CharacterGalleryViewModel.Cards.Count, CharacterGalleryViewModel.IsLoading, CharacterGalleryViewModel.LoadCardsCommand, forceReload);
        AwaitOrLoad(loadTasks, CoordinateGalleryViewModel.Cards.Count, CoordinateGalleryViewModel.IsLoading, CoordinateGalleryViewModel.LoadCardsCommand, forceReload);

        if (loadTasks.Count > 0)
            await Task.WhenAll(loadTasks);

        static void AwaitOrLoad(List<Task> tasks, int cardCount, bool isLoading, CommunityToolkit.Mvvm.Input.IAsyncRelayCommand cmd, bool forceReload)
        {
            if (!forceReload && cardCount > 0) return;
            if (isLoading)
                tasks.Add(cmd.ExecutionTask ?? Task.CompletedTask);
            else
                tasks.Add(cmd.ExecuteAsync(null));
        }
    }
}

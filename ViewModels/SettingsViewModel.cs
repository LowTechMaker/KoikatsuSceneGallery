using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace KoikatsuSceneGallery.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public ObservableCollection<string> FolderPaths { get; } = [];
    public ObservableCollection<string> CharacterFolderPaths { get; } = [];
    public ObservableCollection<string> CoordinateFolderPaths { get; } = [];
    public ObservableCollection<string> AllowedResolutions { get; } = [];
    public ObservableCollection<string> CharacterAllowedResolutions { get; } = [];
    public ObservableCollection<string> CoordinateAllowedResolutions { get; } = [];

    public bool HasNoFolders => FolderPaths.Count == 0;
    public bool HasNoCharacterFolders => CharacterFolderPaths.Count == 0;
    public bool HasNoCoordinateFolders => CoordinateFolderPaths.Count == 0;

    [ObservableProperty]
    public partial bool ResolutionFilterEnabled { get; set; }

    [ObservableProperty]
    public partial bool CharacterResolutionFilterEnabled { get; set; }

    [ObservableProperty]
    public partial bool CoordinateResolutionFilterEnabled { get; set; }

    [ObservableProperty]
    public partial bool ShowFileNames { get; set; } = true;

    [ObservableProperty]
    public partial bool ScrollToTopOnSort { get; set; } = true;

    [ObservableProperty]
    public partial double ThumbnailWidth { get; set; } = 240;

    [ObservableProperty]
    public partial double CacheLength { get; set; } = 4;

    [ObservableProperty]
    public partial bool SizeSelectorEnabled { get; set; } = false;

    [ObservableProperty]
    public partial bool PluginAnalysisEnabled { get; set; }

    [ObservableProperty]
    public partial string CacheFolderPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SauceNaoApiKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowRestartHint { get; set; }

    [ObservableProperty]
    public partial string ImportSubfolder { get; set; } = string.Empty;

    [ObservableProperty]
    public partial double ArtworkSubfolderThreshold { get; set; } = 1;

    // ── OCD ─────────────────────────────────────────────────────
    [ObservableProperty]
    public partial bool UseVisualSimilarity { get; set; }

    [ObservableProperty]
    public partial string AuthorFolderFormat { get; set; } = "{name} ({id})";

    [ObservableProperty]
    public partial string ArtworkFolderFormat { get; set; } = "{title} ({id})";

    [ObservableProperty]
    public partial string UnknownFolderName { get; set; } = "Unknown";

    [ObservableProperty]
    public partial string KoikatsuFolderName { get; set; } = "Koikatsu";

    [ObservableProperty]
    public partial string KoikatsuSunshineFolderName { get; set; } = "KoikatsuSunshine";

    [ObservableProperty]
    public partial string GFolderName { get; set; } = "G";

    [ObservableProperty]
    public partial string R18FolderName { get; set; } = "R-18";

    [ObservableProperty]
    public partial string R18GFolderName { get; set; } = "R-18G";

    // Suppresses the restart hint and save while LoadAsync seeds the initial value.
    private bool _isLoading;

    public string CacheFolderDisplay => string.IsNullOrWhiteSpace(CacheFolderPath)
        ? Services.ThumbnailCacheService.DefaultCacheFolder
        : CacheFolderPath;

    public event Action<bool, HashSet<string>>? ResolutionFilterChanged;
    public event Action<bool, HashSet<string>>? CharacterResolutionFilterChanged;
    public event Action<bool, HashSet<string>>? CoordinateResolutionFilterChanged;
    public event Action<bool>? ShowFileNamesChanged;
    public event Action<bool>? PluginAnalysisEnabledChanged;
    public event Action? SceneFolderPathsChanged;
    public event Action? CharacterFolderPathsChanged;
    public event Action? CoordinateFolderPathsChanged;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        FolderPaths.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasNoFolders));
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                SceneFolderPathsChanged?.Invoke();
        };
        CharacterFolderPaths.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasNoCharacterFolders));
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                CharacterFolderPathsChanged?.Invoke();
        };
        CoordinateFolderPaths.CollectionChanged += (_, e) =>
        {
            OnPropertyChanged(nameof(HasNoCoordinateFolders));
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
                CoordinateFolderPathsChanged?.Invoke();
        };
    }

    partial void OnResolutionFilterEnabledChanged(bool value)
    {
        _ = SaveConfigAsync();
        ResolutionFilterChanged?.Invoke(ResolutionFilterEnabled, [.. AllowedResolutions]);
    }

    partial void OnCharacterResolutionFilterEnabledChanged(bool value)
    {
        _ = SaveConfigAsync();
        CharacterResolutionFilterChanged?.Invoke(CharacterResolutionFilterEnabled, [.. CharacterAllowedResolutions]);
    }

    partial void OnCoordinateResolutionFilterEnabledChanged(bool value)
    {
        _ = SaveConfigAsync();
        CoordinateResolutionFilterChanged?.Invoke(CoordinateResolutionFilterEnabled, [.. CoordinateAllowedResolutions]);
    }

    partial void OnShowFileNamesChanged(bool value)
    {
        _ = SaveConfigAsync();
        ShowFileNamesChanged?.Invoke(value);
    }

    partial void OnScrollToTopOnSortChanged(bool value)
    {
        _ = SaveConfigAsync();
    }

    partial void OnCacheLengthChanged(double value)
    {
        if (_isLoading)
            return;
        _ = SaveConfigAsync();
    }

    partial void OnSizeSelectorEnabledChanged(bool value)
    {
        if (_isLoading)
            return;
        _ = SaveConfigAsync();
    }

    partial void OnPluginAnalysisEnabledChanged(bool value)
    {
        // Suppress while LoadAsync seeds the saved value — only react to the
        // user actually flipping the switch, not to opening the settings page.
        if (_isLoading)
            return;

        _ = SaveConfigAsync();
        PluginAnalysisEnabledChanged?.Invoke(value);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_isLoading)
            return;

        _ = SaveConfigAsync();
        ShowRestartHint = true;
    }

    partial void OnImportSubfolderChanged(string value)
    {
        _ = SaveConfigAsync();
    }

    partial void OnArtworkSubfolderThresholdChanged(double value)
    {
        _ = SaveConfigAsync();
    }

    partial void OnAuthorFolderFormatChanged(string value) => _ = SaveConfigAsync();
    partial void OnArtworkFolderFormatChanged(string value) => _ = SaveConfigAsync();
    partial void OnUnknownFolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnKoikatsuFolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnKoikatsuSunshineFolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnGFolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnR18FolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnR18GFolderNameChanged(string value) => _ = SaveConfigAsync();
    partial void OnSauceNaoApiKeyChanged(string value)
    {
        if (_isLoading)
            return;

        _ = SaveConfigAsync();
    }

    public async Task LoadAsync()
    {
        var config = await _settingsService.LoadConfigAsync();

        FolderPaths.Clear();
        foreach (var path in config.FolderPaths)
            FolderPaths.Add(path);

        CharacterFolderPaths.Clear();
        foreach (var path in config.CharacterFolderPaths)
            CharacterFolderPaths.Add(path);

        AllowedResolutions.Clear();
        foreach (var res in config.AllowedResolutions)
            AllowedResolutions.Add(res);

        CharacterAllowedResolutions.Clear();
        foreach (var res in config.CharacterAllowedResolutions)
            CharacterAllowedResolutions.Add(res);

        CoordinateFolderPaths.Clear();
        foreach (var path in config.CoordinateFolderPaths)
            CoordinateFolderPaths.Add(path);

        CoordinateAllowedResolutions.Clear();
        foreach (var res in config.CoordinateAllowedResolutions)
            CoordinateAllowedResolutions.Add(res);

        ResolutionFilterEnabled = config.ResolutionFilterEnabled;
        CharacterResolutionFilterEnabled = config.CharacterResolutionFilterEnabled;
        CoordinateResolutionFilterEnabled = config.CoordinateResolutionFilterEnabled;
        ShowFileNames = config.ShowFileNames;
        ScrollToTopOnSort = config.ScrollToTopOnSort;
        ThumbnailWidth = config.ThumbnailWidth;
        CacheFolderPath = config.CacheFolderPath;

        ImportSubfolder = config.ImportSubfolder;
        ArtworkSubfolderThreshold = config.ArtworkSubfolderThreshold;
        UseVisualSimilarity = config.UseVisualSimilarity;

        AuthorFolderFormat = config.AuthorFolderFormat;
        ArtworkFolderFormat = config.ArtworkFolderFormat;
        UnknownFolderName = config.UnknownFolderName;
        KoikatsuFolderName = config.KoikatsuFolderName;
        KoikatsuSunshineFolderName = config.KoikatsuSunshineFolderName;
        GFolderName = config.GFolderName;
        R18FolderName = config.R18FolderName;
        R18GFolderName = config.R18GFolderName;

        _isLoading = true;
        PluginAnalysisEnabled = config.PluginAnalysisEnabled;
        SelectedLanguage = config.Language;
        CacheLength = config.CacheLength;
        SizeSelectorEnabled = config.SizeSelectorEnabled;
        SauceNaoApiKey = config.SauceNaoApiKey;
        _isLoading = false;

        OnPropertyChanged(nameof(CacheFolderDisplay));
    }

    [RelayCommand]
    private async Task AddFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null && !FolderPaths.Contains(folder.Path))
        {
            FolderPaths.Add(folder.Path);
            await SaveConfigAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveFolder(string path)
    {
        FolderPaths.Remove(path);
        await SaveConfigAsync();
    }

    [RelayCommand]
    private async Task AddCharacterFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null && !CharacterFolderPaths.Contains(folder.Path))
        {
            CharacterFolderPaths.Add(folder.Path);
            await SaveConfigAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveCharacterFolder(string path)
    {
        CharacterFolderPaths.Remove(path);
        await SaveConfigAsync();
    }

    [RelayCommand]
    private async Task AddCoordinateFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null && !CoordinateFolderPaths.Contains(folder.Path))
        {
            CoordinateFolderPaths.Add(folder.Path);
            await SaveConfigAsync();
        }
    }

    [RelayCommand]
    private async Task RemoveCoordinateFolder(string path)
    {
        CoordinateFolderPaths.Remove(path);
        await SaveConfigAsync();
    }

    [RelayCommand]
    private async Task ChangeCacheFolderAsync()
    {
        var picker = new FolderPicker();
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            CacheFolderPath = folder.Path;
            App.ThumbnailCacheService.SetCacheFolder(folder.Path);
            OnPropertyChanged(nameof(CacheFolderDisplay));
            await SaveConfigAsync();
        }
    }

    [RelayCommand]
    private async Task ResetCacheFolderAsync()
    {
        CacheFolderPath = string.Empty;
        App.ThumbnailCacheService.SetCacheFolder(string.Empty);
        OnPropertyChanged(nameof(CacheFolderDisplay));
        await SaveConfigAsync();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        await App.ThumbnailCacheService.ClearCacheAsync();
    }

    [RelayCommand]
    private void ScanMissingMetadata() => App.GalleryViewModel.ScanMissingMetadata();

    [RelayCommand]
    private void RescanMetadata() => App.GalleryViewModel.RescanAllMetadata();

    [RelayCommand]
    private async Task AddResolution(string input)
    {
        var parsed = ResolutionOption.TryParse(input);
        if (parsed != null && !AllowedResolutions.Contains(parsed.ToString()))
        {
            AllowedResolutions.Add(parsed.ToString());
            await SaveConfigAsync();
            ResolutionFilterChanged?.Invoke(ResolutionFilterEnabled, [.. AllowedResolutions]);
        }
    }

    [RelayCommand]
    private async Task RemoveResolution(string resolution)
    {
        AllowedResolutions.Remove(resolution);
        await SaveConfigAsync();
        ResolutionFilterChanged?.Invoke(ResolutionFilterEnabled, [.. AllowedResolutions]);
    }

    [RelayCommand]
    private async Task AddCharacterResolution(string input)
    {
        var parsed = ResolutionOption.TryParse(input);
        if (parsed != null && !CharacterAllowedResolutions.Contains(parsed.ToString()))
        {
            CharacterAllowedResolutions.Add(parsed.ToString());
            await SaveConfigAsync();
            CharacterResolutionFilterChanged?.Invoke(CharacterResolutionFilterEnabled, [.. CharacterAllowedResolutions]);
        }
    }

    [RelayCommand]
    private async Task RemoveCharacterResolution(string resolution)
    {
        CharacterAllowedResolutions.Remove(resolution);
        await SaveConfigAsync();
        CharacterResolutionFilterChanged?.Invoke(CharacterResolutionFilterEnabled, [.. CharacterAllowedResolutions]);
    }

    [RelayCommand]
    private async Task AddCoordinateResolution(string input)
    {
        var parsed = ResolutionOption.TryParse(input);
        if (parsed != null && !CoordinateAllowedResolutions.Contains(parsed.ToString()))
        {
            CoordinateAllowedResolutions.Add(parsed.ToString());
            await SaveConfigAsync();
            CoordinateResolutionFilterChanged?.Invoke(CoordinateResolutionFilterEnabled, [.. CoordinateAllowedResolutions]);
        }
    }

    [RelayCommand]
    private async Task RemoveCoordinateResolution(string resolution)
    {
        CoordinateAllowedResolutions.Remove(resolution);
        await SaveConfigAsync();
        CoordinateResolutionFilterChanged?.Invoke(CoordinateResolutionFilterEnabled, [.. CoordinateAllowedResolutions]);
    }

    /// <summary>
    /// Persists a new gallery thumbnail width (set via Ctrl+wheel in the
    /// gallery). Called debounced so rapid wheel ticks don't rewrite the file
    /// on every step.
    /// </summary>
    public async Task SaveThumbnailWidthAsync(double width)
    {
        ThumbnailWidth = width;
        await SaveConfigAsync();
    }

    private async Task SaveConfigAsync()
    {
        await _saveLock.WaitAsync();
        try
        {
            var config = new SettingsService.ConfigData
            {
                FolderPaths = [.. FolderPaths],
                CharacterFolderPaths = [.. CharacterFolderPaths],
                CoordinateFolderPaths = [.. CoordinateFolderPaths],
                ResolutionFilterEnabled = ResolutionFilterEnabled,
                AllowedResolutions = [.. AllowedResolutions],
                CharacterResolutionFilterEnabled = CharacterResolutionFilterEnabled,
                CharacterAllowedResolutions = [.. CharacterAllowedResolutions],
                CoordinateResolutionFilterEnabled = CoordinateResolutionFilterEnabled,
                CoordinateAllowedResolutions = [.. CoordinateAllowedResolutions],
                ShowFileNames = ShowFileNames,
                ScrollToTopOnSort = ScrollToTopOnSort,
                ThumbnailWidth = ThumbnailWidth,
                CacheLength = CacheLength,
                SizeSelectorEnabled = SizeSelectorEnabled,
                PluginAnalysisEnabled = PluginAnalysisEnabled,
                CacheFolderPath = CacheFolderPath,
                SauceNaoApiKey = SauceNaoApiKey,
                Language = SelectedLanguage,
                ImportSubfolder = ImportSubfolder,
                ArtworkSubfolderThreshold = (int)ArtworkSubfolderThreshold,
                UseVisualSimilarity = UseVisualSimilarity,
                AuthorFolderFormat = AuthorFolderFormat,
                ArtworkFolderFormat = ArtworkFolderFormat,
                UnknownFolderName = UnknownFolderName,
                KoikatsuFolderName = KoikatsuFolderName,
                KoikatsuSunshineFolderName = KoikatsuSunshineFolderName,
                GFolderName = GFolderName,
                R18FolderName = R18FolderName,
                R18GFolderName = R18GFolderName
            };
            await _settingsService.SaveConfigAsync(config);
        }
        catch (Exception) { }
        finally
        {
            _saveLock.Release();
        }
    }
}

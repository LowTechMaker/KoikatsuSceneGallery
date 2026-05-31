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
    public ObservableCollection<string> AllowedResolutions { get; } = [];

    public bool HasNoFolders => FolderPaths.Count == 0;
    public bool HasNoCharacterFolders => CharacterFolderPaths.Count == 0;

    [ObservableProperty]
    public partial bool ResolutionFilterEnabled { get; set; }

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
    public partial string SelectedLanguage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowRestartHint { get; set; }

    // Suppresses the restart hint and save while LoadAsync seeds the initial value.
    private bool _isLoading;

    public string CacheFolderDisplay => string.IsNullOrWhiteSpace(CacheFolderPath)
        ? Services.ThumbnailCacheService.DefaultCacheFolder
        : CacheFolderPath;

    public event Action<bool, HashSet<string>>? ResolutionFilterChanged;
    public event Action<bool>? ShowFileNamesChanged;
    public event Action<bool>? PluginAnalysisEnabledChanged;

    public SettingsViewModel(SettingsService settingsService)
    {
        _settingsService = settingsService;
        FolderPaths.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoFolders));
        CharacterFolderPaths.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoCharacterFolders));
    }

    partial void OnResolutionFilterEnabledChanged(bool value)
    {
        _ = SaveConfigAsync();
        ResolutionFilterChanged?.Invoke(ResolutionFilterEnabled, [.. AllowedResolutions]);
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

        ResolutionFilterEnabled = config.ResolutionFilterEnabled;
        ShowFileNames = config.ShowFileNames;
        ScrollToTopOnSort = config.ScrollToTopOnSort;
        ThumbnailWidth = config.ThumbnailWidth;
        CacheFolderPath = config.CacheFolderPath;

        _isLoading = true;
        PluginAnalysisEnabled = config.PluginAnalysisEnabled;
        SelectedLanguage = config.Language;
        CacheLength = config.CacheLength;
        SizeSelectorEnabled = config.SizeSelectorEnabled;
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
                ResolutionFilterEnabled = ResolutionFilterEnabled,
                AllowedResolutions = [.. AllowedResolutions],
                ShowFileNames = ShowFileNames,
                ScrollToTopOnSort = ScrollToTopOnSort,
                ThumbnailWidth = ThumbnailWidth,
                CacheLength = CacheLength,
                SizeSelectorEnabled = SizeSelectorEnabled,
                PluginAnalysisEnabled = PluginAnalysisEnabled,
                CacheFolderPath = CacheFolderPath,
                Language = SelectedLanguage
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

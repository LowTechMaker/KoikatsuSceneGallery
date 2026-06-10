using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

/// <summary>Display row for the Settings → Plugins list. Plain get-only class:
/// the XAML type-info generator rejects record init-setters on x:DataType types.</summary>
public sealed class PluginListItem(string name, string version, string statusText, string? error)
{
    public string Name { get; } = name;
    public string Version { get; } = version;
    public string StatusText { get; } = statusText;
    public string? Error { get; } = error;
    public bool HasError => Error != null;
}

public sealed partial class SettingsPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    public SettingsViewModel ViewModel { get; }

    public GalleryViewModel GalleryViewModel => App.GalleryViewModel;

    /// <summary>Plugin list is fixed for the app's lifetime (changes need a restart).</summary>
    public List<PluginListItem> PluginItems { get; }

    public bool HasNoPlugins => PluginItems.Count == 0;

    public SettingsPage()
    {
        ViewModel = App.SettingsViewModel;
        PluginItems = App.PluginService.Plugins
            .Select(p => new PluginListItem(
                p.Name,
                p.Version == "?" ? "" : $"v{p.Version}",
                ResLoader.GetString(p.Status == PluginStatus.Loaded ? "Plugins_StatusLoaded" : "Plugins_StatusFailed"),
                p.Error))
            .ToList();
        InitializeComponent();
    }

    private async void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
    {
        // The folder may not exist yet on a build that shipped without plugins;
        // create it so the button always lands the user somewhere useful.
        Directory.CreateDirectory(PluginService.PluginsDirectory);
        await Launcher.LaunchFolderPathAsync(PluginService.PluginsDirectory);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
            await ViewModel.RemoveFolderCommand.ExecuteAsync(path);
    }

    private async void RemoveCharacterFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
            await ViewModel.RemoveCharacterFolderCommand.ExecuteAsync(path);
    }

    private async void AddResolution_Click(object sender, RoutedEventArgs e)
    {
        await TryAddResolution();
    }

    private async void ResolutionInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            await TryAddResolution();
    }

    private async Task TryAddResolution()
    {
        var input = ResolutionInput.Text.Trim();
        if (!string.IsNullOrEmpty(input))
        {
            await ViewModel.AddResolutionCommand.ExecuteAsync(input);
            ResolutionInput.Text = string.Empty;
        }
    }

    private async void RemoveResolution_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string resolution)
            await ViewModel.RemoveResolutionCommand.ExecuteAsync(resolution);
    }

    private async void AddCharacterResolution_Click(object sender, RoutedEventArgs e)
    {
        await TryAddCharacterResolution();
    }

    private async void CharacterResolutionInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            await TryAddCharacterResolution();
    }

    private async Task TryAddCharacterResolution()
    {
        var input = CharacterResolutionInput.Text.Trim();
        if (!string.IsNullOrEmpty(input))
        {
            await ViewModel.AddCharacterResolutionCommand.ExecuteAsync(input);
            CharacterResolutionInput.Text = string.Empty;
        }
    }

    private async void RemoveCoordinateFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
            await ViewModel.RemoveCoordinateFolderCommand.ExecuteAsync(path);
    }

    private async void AddCoordinateResolution_Click(object sender, RoutedEventArgs e)
    {
        await TryAddCoordinateResolution();
    }

    private async void CoordinateResolutionInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            await TryAddCoordinateResolution();
    }

    private async Task TryAddCoordinateResolution()
    {
        var input = CoordinateResolutionInput.Text.Trim();
        if (!string.IsNullOrEmpty(input))
        {
            await ViewModel.AddCoordinateResolutionCommand.ExecuteAsync(input);
            CoordinateResolutionInput.Text = string.Empty;
        }
    }

    private async void RemoveCoordinateResolution_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string resolution)
            await ViewModel.RemoveCoordinateResolutionCommand.ExecuteAsync(resolution);
    }

    private async void RemoveCharacterResolution_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string resolution)
            await ViewModel.RemoveCharacterResolutionCommand.ExecuteAsync(resolution);
    }

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ClearCacheCommand.ExecuteAsync(null);
    }

    private void RestartApp_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }
}

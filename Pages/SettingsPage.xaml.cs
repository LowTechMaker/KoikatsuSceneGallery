using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        ViewModel = App.SettingsViewModel;
        InitializeComponent();
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

    private async void ClearCache_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ClearCacheCommand.ExecuteAsync(null);
    }

    private void RestartApp_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);
    }
}

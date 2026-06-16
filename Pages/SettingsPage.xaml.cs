using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using SceneGallery.PluginSdk;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

/// <summary>Display row for the Settings → Plugins list. Plain get-only class:
/// the XAML type-info generator rejects record init-setters on x:DataType types.</summary>
public sealed class PluginListItem(
    string name,
    string version,
    string statusText,
    string? error,
    string? description,
    string filePath,
    string? updateText = null,
    string? downloadUrl = null,
    string? changelog = null)
{
    public string Name { get; } = name;
    public string Version { get; } = version;
    public bool HasVersion => !string.IsNullOrWhiteSpace(Version);
    public string StatusText { get; } = statusText;
    public string? Error { get; } = error;
    public bool HasError => Error != null;
    public string? Description { get; } = description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string FilePath { get; } = filePath;
    public bool UpdateAvailable => UpdateText is not null;
    public string? UpdateText { get; } = updateText;
    public string? DownloadUrl { get; } = downloadUrl;
    public bool HasDownloadUrl => !string.IsNullOrWhiteSpace(DownloadUrl);
    public string? Changelog { get; } = changelog;
    public bool HasChangelog => !string.IsNullOrWhiteSpace(Changelog);
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
        var updateFmt = ResLoader.GetString("Plugins_UpdateAvailable");
        PluginItems = App.PluginService.Plugins
            .Select(p => new PluginListItem(
                p.Name,
                p.Version == "?" ? "" : $"v{p.Version}",
                ResLoader.GetString(p.Status == PluginStatus.Loaded ? "Plugins_StatusLoaded" : "Plugins_StatusFailed"),
                p.Error,
                p.Description,
                p.FilePath,
                p.AvailableVersion is not null
                    ? string.Format(updateFmt, p.AvailableVersion)
                    : null,
                p.AvailableDownloadUrl,
                p.Changelog))
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

    private async void PluginItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: PluginListItem item })
            await ShowPluginSettingsDialogAsync(item);
    }

    private async Task ShowPluginSettingsDialogAsync(PluginListItem item)
    {
        var panel = new StackPanel { Spacing = 16, MinWidth = 420, MaxWidth = 720 };
        var settingsProvider = App.PluginService.GetSettingsProvider(item.Name);
        var editors = new Dictionary<string, FrameworkElement>();
        InfoBar? validationBar = null;

        if (item.HasDescription)
        {
            panel.Children.Add(new TextBlock
            {
                Text = item.Description,
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var details = new StackPanel { Spacing = 8 };
        details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailStatus"), item.StatusText));

        if (item.HasVersion)
            details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailVersion"), item.Version));

        details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailPath"), item.FilePath));

        if (item.UpdateAvailable)
            details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailUpdate"), item.UpdateText!));

        if (item.HasDownloadUrl)
            details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailDownload"), item.DownloadUrl!));

        if (item.HasChangelog)
            details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailChangelog"), item.Changelog!));

        if (item.HasError)
            details.Children.Add(BuildDetailRow(ResLoader.GetString("Plugins_DetailError"), item.Error!));

        panel.Children.Add(details);

        if (settingsProvider is not null && settingsProvider.Settings.Count > 0)
        {
            validationBar = new InfoBar
            {
                IsOpen = false,
                IsClosable = false,
                Severity = InfoBarSeverity.Error,
            };
            panel.Children.Add(validationBar);

            foreach (var setting in settingsProvider.Settings)
            {
                if (setting.ValueType == PluginSettingValueType.Action)
                {
                    var actionBtn = new Button
                    {
                        Content = setting.DefaultValue ?? setting.Label,
                        HorizontalAlignment = HorizontalAlignment.Left,
                    };
                    actionBtn.Click += (_, _) =>
                    {
                        settingsProvider.SetSettingValue(setting.Key, null);
                        actionBtn.Content = "✔ " + (setting.DefaultValue ?? setting.Label);
                    };
                    panel.Children.Add(BuildSettingRow(setting, actionBtn));
                    continue;
                }

                var value = settingsProvider.GetSettingValue(setting.Key) ?? setting.DefaultValue;
                var editor = BuildSettingEditor(setting, value);
                editors[setting.Key] = editor;
                panel.Children.Add(BuildSettingRow(setting, editor));
            }
        }
        else
        {
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Informational,
                Title = ResLoader.GetString("Plugins_NoSettingsTitle"),
                Message = ResLoader.GetString("Plugins_NoSettingsMessage"),
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = string.Format(ResLoader.GetString("Plugins_SettingsTitle"), item.Name),
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 620,
            },
            CloseButtonText = ResLoader.GetString("Plugins_Close"),
            PrimaryButtonText = editors.Count > 0 ? ResLoader.GetString("Plugins_Save") : "",
            DefaultButton = ContentDialogButton.Close,
        };

        if (settingsProvider is not null && validationBar is not null)
        {
            dialog.PrimaryButtonClick += (_, args) =>
            {
                try
                {
                    foreach (var (key, editor) in editors)
                        settingsProvider.SetSettingValue(key, ReadSettingEditorValue(editor));
                }
                catch (Exception ex)
                {
                    validationBar.Message = ex.Message;
                    validationBar.IsOpen = true;
                    args.Cancel = true;
                }
            };
        }

        await dialog.ShowAsync();
    }

    private static FrameworkElement BuildDetailRow(string label, string value)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(new TextBlock
        {
            Text = label,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });

        var valueBlock = new TextBlock
        {
            Text = value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(valueBlock, 1);
        grid.Children.Add(valueBlock);

        return grid;
    }

    private static FrameworkElement BuildSettingRow(PluginSettingDefinition setting, FrameworkElement editor)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = setting.Label,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });

        if (!string.IsNullOrWhiteSpace(setting.Description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = setting.Description,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
        }

        panel.Children.Add(editor);
        return panel;
    }

    private static FrameworkElement BuildSettingEditor(PluginSettingDefinition setting, string? value)
    {
        return setting.ValueType switch
        {
            PluginSettingValueType.Boolean => new ToggleSwitch
            {
                IsOn = bool.TryParse(value, out var isOn) && isOn,
            },
            PluginSettingValueType.Secret => new PasswordBox
            {
                Password = value ?? "",
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            PluginSettingValueType.Integer => new NumberBox
            {
                Value = double.TryParse(value, out var number) ? number : double.NaN,
                MinWidth = 180,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
            _ => new TextBox
            {
                Text = value ?? "",
                MinWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Left,
            },
        };
    }

    private static string? ReadSettingEditorValue(FrameworkElement editor)
    {
        return editor switch
        {
            ToggleSwitch toggle => toggle.IsOn.ToString(),
            NumberBox number => double.IsNaN(number.Value) ? null : ((int)number.Value).ToString(),
            PasswordBox passwordBox => passwordBox.Password,
            TextBox textBox => textBox.Text,
            _ => null,
        };
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

    private async void RemoveScreenshotFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
            await ViewModel.RemoveScreenshotFolderCommand.ExecuteAsync(path);
    }

    private async void RemoveVideoFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.CommandParameter is string path)
            await ViewModel.RemoveVideoFolderCommand.ExecuteAsync(path);
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

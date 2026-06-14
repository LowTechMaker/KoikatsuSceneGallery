using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ImportPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    public ImportViewModel ViewModel { get; }

    private readonly IReadOnlyList<ICookieSetupProvider> _cookieSetupProviders;

    public ImportPage()
    {
        ViewModel = App.ImportViewModel!;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _cookieSetupProviders = App.PluginService.CookieSetupProviders;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;

        if (_cookieSetupProviders.Count > 0)
            CookieSetupButton.Visibility = Visibility.Visible;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportViewModel.ShowRejectedWarning) && ViewModel.ShowRejectedWarning)
        {
            RejectedWarningBar.Message = string.Format(
                ResLoader.GetString("Import_RejectedWarningMessage"),
                ViewModel.RejectedCount);
        }
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var storageItems = await e.DataView.GetStorageItemsAsync();
        var paths = new List<string>();

        foreach (var item in storageItems)
        {
            if (item is Windows.Storage.StorageFile file)
            {
                if (!string.IsNullOrEmpty(file.Path))
                    paths.Add(file.Path);
            }
            else if (item is Windows.Storage.StorageFolder folder && !string.IsNullOrEmpty(folder.Path))
            {
                var folderPath = folder.Path;
                await Task.Run(() =>
                {
                    try
                    {
                        foreach (var p in Directory.EnumerateFiles(folderPath, "*.png", SearchOption.AllDirectories))
                            paths.Add(p);
                    }
                    catch { }
                });
            }
        }

        if (paths.Count > 0 && await EnsureRequiredCookieSetupAsync(paths))
            await ViewModel.AddFilesCommand.ExecuteAsync(paths);
    }

    private async void AssignAuthor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ImportArtworkGroup group })
            await ViewModel.AssignAuthorCommand.ExecuteAsync(group);
    }

    private ImportArtworkGroup? _pickTarget;
    private bool _pickBatchUnknownAuthor;
    private bool _pickBatchFetchFailedAuthor;

    private void PickAuthor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ImportArtworkGroup group } btn) return;
        _pickTarget = group;
        _pickTargetUnknownGroup = null;
        _pickBatchUnknownAuthor = false;
        _pickBatchFetchFailedAuthor = false;

        ShowAuthorPickerFlyout(btn);
    }

    private ImportUnknownGroup? _pickTargetUnknownGroup;

    private void PickAuthorForUnknownGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ImportUnknownGroup group } btn) return;
        _pickTargetUnknownGroup = group;
        _pickTarget = null;
        _pickBatchUnknownAuthor = false;
        _pickBatchFetchFailedAuthor = false;

        ShowAuthorPickerFlyout(btn);
    }

    private void PickAuthorForUnknownBatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _pickTarget = null;
        _pickTargetUnknownGroup = null;
        _pickBatchUnknownAuthor = true;
        _pickBatchFetchFailedAuthor = false;

        ShowAuthorPickerFlyout(btn);
    }

    private void PickAuthorForFetchFailedBatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        _pickTarget = null;
        _pickTargetUnknownGroup = null;
        _pickBatchUnknownAuthor = false;
        _pickBatchFetchFailedAuthor = true;

        ShowAuthorPickerFlyout(btn);
    }

    private Flyout? _authorFlyout;

    private List<SelectableAuthor> BuildFilteredList(string? query)
    {
        var result = new List<SelectableAuthor>();
        bool Filter(SelectableAuthor a) =>
            string.IsNullOrEmpty(query)
            || a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || a.Id.Contains(query, StringComparison.OrdinalIgnoreCase);

        result.AddRange(ViewModel.BatchAuthors.Where(Filter));
        result.AddRange(ViewModel.LibraryAuthors.Where(Filter));
        return result;
    }

    private int _batchVisibleCount;

    private void ShowAuthorPickerFlyout(Button anchor)
    {
        var template = (DataTemplate)Resources["AuthorPickerItemTemplate"];

        var listView = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 400,
            MinWidth = 280,
            ItemTemplate = template,
        };

        var allItems = BuildFilteredList(null);
        _batchVisibleCount = ViewModel.BatchAuthors.Count;
        listView.ItemsSource = allItems;
        listView.SelectionChanged += AuthorPicker_SelectionChanged;

        var searchBox = new AutoSuggestBox
        {
            PlaceholderText = ResLoader.GetString("Import_SearchAuthor"),
            QueryIcon = new SymbolIcon(Symbol.Find),
            Margin = new Thickness(0, 0, 0, 8),
        };
        searchBox.TextChanged += (s, _) =>
        {
            var query = s.Text.Trim();
            var filtered = BuildFilteredList(string.IsNullOrEmpty(query) ? null : query);
            _batchVisibleCount = string.IsNullOrEmpty(query)
                ? ViewModel.BatchAuthors.Count
                : ViewModel.BatchAuthors.Count(a =>
                    a.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || a.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
            listView.ItemsSource = filtered;
        };

        // Group header / separator via ContainerContentChanging
        listView.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemIndex == _batchVisibleCount && _batchVisibleCount > 0)
                args.ItemContainer.BorderThickness = new Thickness(0, 1, 0, 0);
            else
                args.ItemContainer.BorderThickness = new Thickness(0);

            args.ItemContainer.BorderBrush =
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"];
        };

        var panel = new StackPanel();
        panel.Children.Add(searchBox);
        panel.Children.Add(listView);

        _authorFlyout = new Flyout
        {
            Content = panel,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
        };
        _authorFlyout.ShowAt(anchor);
    }

    private async void AuthorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView || e.AddedItems.Count == 0 || e.AddedItems[0] is not SelectableAuthor author) return;

        _authorFlyout?.Hide();

        if (_pickTarget is not null)
        {
            _pickTarget.ManualAuthorId = author.Id;
            await ViewModel.AssignAuthorCommand.ExecuteAsync(_pickTarget);
            _pickTarget = null;
        }
        else if (_pickTargetUnknownGroup is not null)
        {
            _pickTargetUnknownGroup.ManualAuthorId = author.Id;
            await ViewModel.AssignAuthorToUnknownGroupCommand.ExecuteAsync(_pickTargetUnknownGroup);
            _pickTargetUnknownGroup = null;
        }
        else if (_pickBatchUnknownAuthor)
        {
            ViewModel.BatchManualAuthorId = author.Id;
            await ViewModel.AssignBatchAuthorIdToUnknownCommand.ExecuteAsync(null);
            _pickBatchUnknownAuthor = false;
        }
        else if (_pickBatchFetchFailedAuthor)
        {
            ViewModel.BatchFetchFailedAuthorId = author.Id;
            await ViewModel.AssignBatchAuthorIdToFetchFailedCommand.ExecuteAsync(null);
            _pickBatchFetchFailedAuthor = false;
        }
    }

    private async void AssignAuthorToUnknownGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ImportUnknownGroup group })
            await ViewModel.AssignAuthorToUnknownGroupCommand.ExecuteAsync(group);
    }

    private async void AssignArtworkIdToUnknownGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ImportUnknownGroup group })
            await ViewModel.AssignArtworkIdToUnknownGroupCommand.ExecuteAsync(group);
    }

    private async void SearchSauceNaoForFetchFailed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: ImportArtworkGroup group } button)
            return;

        button.IsEnabled = false;
        group.IsSauceNaoSearching = true;
        try
        {
            var result = await ViewModel.SearchSauceNaoForFetchFailedGroupAsync(group, CancellationToken.None);
            if (result is null)
            {
                await ShowMessageDialog(
                    ResLoader.GetString("Import_SauceNaoNoResultTitle"),
                    ResLoader.GetString("Import_SauceNaoNoResultMessage"));
                return;
            }

            var rating = await ShowSauceNaoResultDialog(result);
            if (rating is null)
                return;

            await ViewModel.ApplySauceNaoResultToFetchFailedGroupAsync(group, result, rating.Value);
        }
        catch (InvalidOperationException)
        {
            await ShowMessageDialog(
                ResLoader.GetString("Import_SauceNaoApiKeyMissingTitle"),
                ResLoader.GetString("Import_SauceNaoApiKeyMissingMessage"));
        }
        finally
        {
            group.IsSauceNaoSearching = false;
            button.IsEnabled = true;
        }
    }

    private async Task<ContentRating?> ShowSauceNaoResultDialog(ReverseImageSearchResult result)
    {
        var ratingBox = new ComboBox
        {
            MinWidth = 180,
            SelectedIndex = 0,
        };
        ratingBox.Items.Add(new ComboBoxItem { Content = "G", Tag = ContentRating.AllAges });
        ratingBox.Items.Add(new ComboBoxItem { Content = "R-18", Tag = ContentRating.R18 });
        ratingBox.Items.Add(new ComboBoxItem { Content = "R-18G", Tag = ContentRating.R18G });

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(ResLoader.GetString("Import_SauceNaoAuthor"), result.AuthorName, result.AuthorId),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(result.ThumbnailUrl)
            && Uri.TryCreate(result.ThumbnailUrl, UriKind.Absolute, out var thumbnailUri))
        {
            panel.Children.Add(new Border
            {
                Width = 260,
                Height = 180,
                CornerRadius = new CornerRadius(6),
                Child = new Image
                {
                    Source = new BitmapImage(thumbnailUri),
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                },
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = string.Format(
                ResLoader.GetString("Import_SauceNaoTitle"),
                string.IsNullOrWhiteSpace(result.Title) ? "-" : result.Title),
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(ResLoader.GetString("Import_SauceNaoSimilarity"), result.Similarity),
        });

        if (result.Similarity < 50)
        {
            panel.Children.Add(new InfoBar
            {
                IsOpen = true,
                IsClosable = false,
                Severity = InfoBarSeverity.Warning,
                Title = ResLoader.GetString("Import_SauceNaoLowSimilarityTitle"),
                Message = ResLoader.GetString("Import_SauceNaoLowSimilarityMessage"),
            });
        }

        panel.Children.Add(new TextBlock { Text = ResLoader.GetString("Import_SauceNaoRating") });
        panel.Children.Add(ratingBox);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Import_SauceNaoResultTitle"),
            Content = panel,
            PrimaryButtonText = ResLoader.GetString("Import_SauceNaoImportButton"),
            CloseButtonText = ResLoader.GetString("Import_SauceNaoCancelButton"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var response = await dialog.ShowAsync();
        if (response != ContentDialogResult.Primary)
            return null;

        return ratingBox.SelectedItem is ComboBoxItem { Tag: ContentRating rating }
            ? rating
            : ContentRating.AllAges;
    }

    private async Task ShowMessageDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = message,
            CloseButtonText = ResLoader.GetString("Import_SauceNaoCloseButton"),
        };
        await dialog.ShowAsync();
    }

    private void RemoveUnknownGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ImportUnknownGroup group })
            ViewModel.RemoveUnknownGroupCommand.Execute(group);
    }

    private void RemoveUnknownItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: ImportItem item })
            ViewModel.RemoveUnknownItemCommand.Execute(item);
    }

    private async void CookieSetup_Click(object sender, RoutedEventArgs e)
    {
        var provider = _cookieSetupProviders.FirstOrDefault(p => p.NeedsCookieSetup)
            ?? _cookieSetupProviders.FirstOrDefault();
        if (provider is null) return;

        await ShowCookieSetupDialogAsync(provider);
    }

    private async Task<bool> EnsureRequiredCookieSetupAsync(IReadOnlyList<string> filePaths)
    {
        while (await FindRequiredCookieSetupProviderAsync(filePaths) is { } provider)
        {
            if (!await ShowCookieSetupDialogAsync(provider))
                return false;
        }

        return true;
    }

    private async Task<ICookieSetupProvider?> FindRequiredCookieSetupProviderAsync(IReadOnlyList<string> filePaths)
    {
        foreach (var provider in _cookieSetupProviders)
        {
            if (!AppliesToAnyPath(provider, filePaths))
                continue;

            if (provider.NeedsCookieSetup)
                return provider;

            if (provider is ICookieSetupValidator validator
                && !await validator.HasUsableCookiesAsync(CancellationToken.None))
            {
                return provider;
            }
        }

        return null;
    }

    private static bool AppliesToAnyPath(ICookieSetupProvider provider, IReadOnlyList<string> filePaths)
    {
        if (provider is not ICardImportProvider importProvider)
            return true;

        return filePaths.Any(path => importProvider.TryParseFilename(Path.GetFileName(path)) is not null);
    }

    private Task<bool> ShowCookieSetupDialogAsync(ICookieSetupProvider provider)
        => CookieSetupDialogService.ShowAsync(
            XamlRoot,
            DispatcherQueue,
            provider,
            ResLoader.GetString("Import_SauceNaoCloseButton"));
}

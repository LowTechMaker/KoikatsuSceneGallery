using System.IO;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ImportPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    public ImportViewModel ViewModel { get; }

    public ImportPage()
    {
        ViewModel = App.ImportViewModel!;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
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

        if (paths.Count > 0)
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
}

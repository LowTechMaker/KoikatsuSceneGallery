using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.System;

namespace KoikatsuSceneGallery.Pages;

/// <summary>
/// Collects privately shared cards: drop files at the top, then drag the
/// folder onto one of the sources below to file them there.
/// </summary>
public sealed partial class LocalSourcesPage : Page
{
    /// <summary>
    /// Marks our own drag so drop handlers can tell it from files dragged in
    /// from Explorer. The batch itself stays in the view model; the package
    /// carries only a token identifying it.
    /// </summary>
    private const string StagedBatchFormat = "KoikatsuSceneGallery/StagedLocalBatch";

    private static readonly ResourceLoader ResLoader = new();

    private readonly StagedCardFlightAnimator _animator;

    public LocalImportViewModel ViewModel { get; } =
        App.Services.GetRequiredService<LocalImportViewModel>();

    public LocalSourcesPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        _animator = new StagedCardFlightAnimator(StagedStrip, DragFolder);

        // Subscribed once, in the constructor: the page is cached and the view
        // model is a singleton, so hooking this up per navigation would report
        // one import several times over.
        ViewModel.DuplicatesKept += OnDuplicatesKept;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The page is cached, so a batch — and a half-finished animation —
        // can outlive a navigation.
        _animator.Reset();
        ViewModel.RevalidateStaging();
    }

    // ── Staging ───────────────────────────────────────────────────

    private void PickFiles_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "LocalSources.PickFiles", async () =>
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));

            var files = await picker.PickMultipleFilesAsync();
            var paths = files.Select(file => file.Path).ToArray();
            if (paths.Length > 0)
                await ViewModel.StageAsync(paths);
        });

    private void ClearStaging_Click(object sender, RoutedEventArgs e) => ViewModel.ClearStaging();

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        // Our own folder must not land back in staging.
        if (e.DataView.Contains(StagedBatchFormat) || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = ResLoader.GetString("LocalSources_DropZoneTitle/Text");
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e) => DropZone_DragOver(sender, e);

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "LocalSources.Drop", async () =>
        {
            if (e.DataView.Contains(StagedBatchFormat) || !e.DataView.Contains(StandardDataFormats.StorageItems))
                return;

            var paths = await CollectDroppedPngPathsAsync(await e.DataView.GetStorageItemsAsync());
            if (paths.Count > 0)
                await ViewModel.StageAsync(paths);
        });

    /// <summary>
    /// Flattens a drop into PNG paths, walking dropped folders.
    /// </summary>
    /// <remarks>
    /// Enumeration returns its result rather than appending to a shared list
    /// from the background thread, and skips directories it cannot read so one
    /// of them does not discard everything already found.
    /// </remarks>
    private static async Task<IReadOnlyList<string>> CollectDroppedPngPathsAsync(
        IReadOnlyList<IStorageItem> items)
    {
        var paths = new List<string>();

        foreach (var item in items)
        {
            switch (item)
            {
                case StorageFile file when !string.IsNullOrEmpty(file.Path):
                    paths.Add(file.Path);
                    break;

                case StorageFolder folder when !string.IsNullOrEmpty(folder.Path):
                    var folderPath = folder.Path;
                    paths.AddRange(await Task.Run(() =>
                    {
                        try
                        {
                            return Directory.EnumerateFiles(folderPath, "*.png", new EnumerationOptions
                            {
                                RecurseSubdirectories = true,
                                IgnoreInaccessible = true,
                            }).ToArray();
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            App.Services.GetRequiredService<IAppLogger>()
                                .LogError("LocalSources.EnumerateDroppedFolder", ex, folderPath);
                            return [];
                        }
                    }));
                    break;
            }
        }

        return paths;
    }

    // ── The folder as a drag source ───────────────────────────────

    private void DragFolder_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (!ViewModel.CanAssign)
        {
            args.Cancel = true;
            return;
        }

        // A package with no format aborts the drag, and targets never see it.
        args.Data.SetData(StagedBatchFormat, ViewModel.StagedBatchToken);
        args.Data.RequestedOperation = DataPackageOperation.Move;

        // After the package is set, so a throw above cannot leave the strip
        // animated with no drag under way. The drag itself is asynchronous, so
        // this renders alongside it.
        _animator.FlyIn(CollectStagedThumbnails());
    }

    private void DragFolder_DropCompleted(UIElement sender, DropCompletedEventArgs args)
    {
        // The single "the drag ended" hook. DragLeave fires on every boundary
        // crossing and would undo the gesture mid-flight.
        if (args.DropResult == DataPackageOperation.Move)
        {
            // A source accepted it; the import is already running and the
            // batch is about to be replaced.
            return;
        }

        _animator.FlyBack();
    }

    /// <summary>
    /// The thumbnail elements currently realized in the strip, in order.
    /// </summary>
    private List<FrameworkElement> CollectStagedThumbnails()
    {
        var thumbnails = new List<FrameworkElement>(LocalImportViewModel.MaxStripThumbnails);
        foreach (var card in ViewModel.VisibleStagedCards)
        {
            if (StagedStripItems.ContainerFromItem(card) is FrameworkElement container)
                thumbnails.Add(container);
        }

        return thumbnails;
    }

    private void ImportToFlyout_Opening(object? sender, object e)
    {
        // Keyboard path for the same action as the drop, rebuilt each time so
        // it matches the current sources.
        ImportToFlyout.Items.Clear();
        foreach (var tile in ViewModel.Tiles)
        {
            var item = new MenuFlyoutItem
            {
                Text = tile is LocalSourceTile source
                    ? source.Summary.Display.Name
                    : ResLoader.GetString("LocalSources_AddSource/Text"),
                Tag = tile,
                IsEnabled = ViewModel.CanAssign,
            };
            item.Click += ImportToMenuItem_Click;
            ImportToFlyout.Items.Add(item);
        }
    }

    private void ImportToMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: { } tile })
            AssignTo(tile);
    }

    // ── Sources as drop targets ───────────────────────────────────

    private void SourceTile_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StagedBatchFormat) || !ViewModel.CanAssign)
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = string.Format(
            ResLoader.GetString("LocalSources_DropCaption"),
            DisplayNameOf(sender));
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private void SourceTile_DragEnter(object sender, DragEventArgs e)
    {
        SourceTile_DragOver(sender, e);
        SetDropHighlight(sender, e.AcceptedOperation != DataPackageOperation.None);
    }

    private void SourceTile_DragLeave(object sender, DragEventArgs e) => SetDropHighlight(sender, false);

    private void SourceTile_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(sender, false);
        if (!e.DataView.Contains(StagedBatchFormat))
            return;

        // Accept before the import runs, so the drag reports Move to the
        // source and the folder does not spring back.
        e.AcceptedOperation = DataPackageOperation.Move;
        e.Handled = true;

        if (sender is FrameworkElement { Tag: { } tile })
            AssignTo(tile);
    }

    private static void SetDropHighlight(object sender, bool visible)
    {
        if (sender is DependencyObject root
            && VisualTreeSearch.FindDescendantByName<Border>(root, "DropHighlight") is { } highlight)
        {
            highlight.Opacity = visible ? 1 : 0;
        }
    }

    private string DisplayNameOf(object sender)
        => sender is FrameworkElement { Tag: LocalSourceTile tile }
            ? tile.Summary.Display.Name
            : ResLoader.GetString("LocalSources_AddSource/Text");

    // ── Assignment ────────────────────────────────────────────────

    private void AssignTo(object tile)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "LocalSources.Assign", async () =>
        {
            var target = tile switch
            {
                LocalSourceTile source => ResolveEntry(source),
                AddLocalSourceTile => await CreateSourceAsync(),
                _ => null,
            };

            if (target is not null)
                await ViewModel.ImportStagedToAsync(target);

            // The drop reported Move, so the strip is already flown into the
            // folder. Anything that leaves the batch in place — a cancelled
            // name dialog, a refused import — has to bring it back, or the
            // cards stay staged but invisible.
            if (ViewModel.HasStagedCards)
                _animator.Reset();
        });

    /// <summary>
    /// The tile's entry is a snapshot; take the registry's current one when it
    /// still knows the source, so a rename between drag and drop is honoured.
    /// </summary>
    private LocalSourceEntry ResolveEntry(LocalSourceTile tile)
        => App.Services.GetRequiredService<LocalSourceRegistry>().Find(tile.Entry.Id) ?? tile.Entry;

    private async Task<LocalSourceEntry?> CreateSourceAsync()
    {
        var input = new TextBox
        {
            PlaceholderText = ResLoader.GetString("LocalSources_NewSourceNamePlaceholder"),
            AcceptsReturn = false,
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("LocalSources_NewSourceTitle"),
            Content = input,
            PrimaryButtonText = ResLoader.GetString("LocalSources_NewSourceConfirm"),
            CloseButtonText = ResLoader.GetString("LocalSources_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var name = input.Text.Trim();
        return name.Length == 0 ? null : ViewModel.CreatePendingSource(name);
    }

    // ── Duplicates left behind ────────────────────────────────────

    /// <summary>How many duplicate names the dialog lists before it counts.</summary>
    private const int MaxListedDuplicates = 12;

    /// <summary>
    /// How many folders the open button reveals. A batch dragged in from
    /// several folders would otherwise open an Explorer window per folder.
    /// </summary>
    private const int MaxRevealedFolders = 3;

    private void OnDuplicatesKept(IReadOnlyList<string> paths)
        => UiEventGuard.Run(
            App.Services.GetRequiredService<IAppLogger>(),
            "LocalSources.Duplicates",
            () => ShowDuplicatesAsync(paths));

    /// <summary>
    /// Names the cards the library already had, and offers to show the user
    /// where those files still are.
    /// </summary>
    private async Task ShowDuplicatesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0 || XamlRoot is null)
            return;

        var names = paths.Select(Path.GetFileName).OfType<string>().ToArray();
        var listed = string.Join(Environment.NewLine, names.Take(MaxListedDuplicates));
        if (names.Length > MaxListedDuplicates)
        {
            listed += Environment.NewLine + string.Format(
                ResLoader.GetString("LocalSources_Duplicates_More"),
                names.Length - MaxListedDuplicates);
        }

        var body = new StackPanel { Spacing = 8, Width = 420 };
        body.Children.Add(new TextBlock
        {
            Text = ResLoader.GetString("LocalSources_Duplicates_Body"),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new ScrollViewer
        {
            MaxHeight = 260,
            Content = new TextBlock
            {
                Text = listed,
                IsTextSelectionEnabled = true,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                FontSize = 12,
            },
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = string.Format(
                ResLoader.GetString("LocalSources_Duplicates_Title"),
                paths.Count),
            Content = body,
            PrimaryButtonText = ResLoader.GetString("LocalSources_Duplicates_OpenFolder"),
            CloseButtonText = ResLoader.GetString("LocalSources_Edit_Close"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await RevealAsync(paths);
    }

    /// <summary>
    /// Opens the folders holding <paramref name="paths"/>, selecting the files
    /// themselves where Windows allows it.
    /// </summary>
    private static async Task RevealAsync(IReadOnlyList<string> paths)
    {
        var folders = paths
            .GroupBy(Path.GetDirectoryName, StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrEmpty(group.Key))
            .Take(MaxRevealedFolders);

        foreach (var folder in folders)
        {
            var options = new FolderLauncherOptions();
            foreach (var path in folder)
            {
                try
                {
                    options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                catch (Exception)
                {
                    // A file that vanished between the import and the click is
                    // no reason not to open its folder.
                }
            }

            await Launcher.LaunchFolderPathAsync(folder.Key!, options);
        }
    }

    private void EditSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LocalSourceTile tile })
            return;

        UiEventGuard.Run(
            App.Services.GetRequiredService<IAppLogger>(),
            "LocalSources.EditSource",
            () => LocalSourceEditing.RunAsync(XamlRoot, tile.Entry.Id));
    }

    private void SourcesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        switch (e.ClickedItem)
        {
            case LocalSourceTile tile:
                Frame.Navigate(typeof(AuthorDetailPage), new AuthorDetailNavigationParameter(tile.Summary));
                break;
            case AddLocalSourceTile:
                UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "LocalSources.NewSource",
                    async () => await CreateSourceAsync());
                break;
        }
    }
}

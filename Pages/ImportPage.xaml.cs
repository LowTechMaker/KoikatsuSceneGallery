using System.IO;
using KoikatsuSceneGallery.Controls;
using KoikatsuSceneGallery.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Microsoft.Windows.ApplicationModel.Resources;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
        ViewModel = App.Services.GetService<ImportViewModel>()!;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        _cookieSetupProviders = App.Services.GetRequiredService<PluginService>().CookieSetupProviders;
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

    // Translate, scale and opacity are all composition-driven, so these storyboards
    // run off the UI thread and need no EnableDependentAnimation.
    private readonly CompositeTransform _addFilesCardTransform = new();
    private Storyboard? _addFilesCardEntrance;
    private Storyboard? _addFilesCardCollapse;
    private TaskCompletionSource? _addFilesCardCollapseCompletion;

    // Bumped on every open. An accept animation that began under an earlier
    // opening must not go on to close the card the user has since reopened.
    private int _addFilesOverlayGeneration;

    private Storyboard BuildAddFilesCardEntrance()
    {
        var storyboard = new Storyboard();
        storyboard.Children.Add(BuildCardAnimation(
            "TranslateY", from: 24, to: 0, milliseconds: 220,
            easing: new CubicEase { EasingMode = EasingMode.EaseOut }));
        storyboard.Children.Add(BuildCardOpacityAnimation(from: 0, to: 1, milliseconds: 140));
        return storyboard;
    }

    private DoubleAnimation BuildCardAnimation(
        string property, double from, double to, int milliseconds, EasingFunctionBase? easing = null)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = easing,
        };
        Storyboard.SetTarget(animation, _addFilesCardTransform);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private DoubleAnimation BuildCardOpacityAnimation(double from, double to, int milliseconds)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
        };
        Storyboard.SetTarget(animation, AddFilesOverlayCard);
        Storyboard.SetTargetProperty(animation, "Opacity");
        return animation;
    }

    private void ShowAddFilesOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsImporting) return;
        _addFilesOverlayGeneration++;
        AddFilesOverlay.Visibility = Visibility.Visible;
        PositionAddFilesCard();
        ResetAddFilesCard();

        _addFilesCardEntrance ??= BuildAddFilesCardEntrance();
        _addFilesCardEntrance.Begin();

        // Focused so Escape reaches the accelerator and Tab lands inside the card.
        AddFilesOverlayCard.Focus(FocusState.Programmatic);
    }

    // A completed storyboard holds its final value, so both must be stopped before
    // the base values below take effect.
    private void ResetAddFilesCard()
    {
        _addFilesCardEntrance?.Stop();
        StopAddFilesCardCollapse();
        AddFilesOverlayCard.RenderTransform = _addFilesCardTransform;
        _addFilesCardTransform.TranslateX = 0;
        _addFilesCardTransform.TranslateY = 0;
        _addFilesCardTransform.ScaleX = 1;
        _addFilesCardTransform.ScaleY = 1;
        AddFilesOverlayCard.Opacity = 1;
        AddFilesOverlayContent.OpenBox();
    }

    /// <summary>
    /// Seals the box, then shrinks the whole card into the button that opened it.
    /// Awaited before the files are handed to the view model so the analysis spinner
    /// does not disable the workspace mid-animation.
    /// </summary>
    /// <remarks>
    /// Always completes, even when the user closes or reopens the card part way
    /// through — the callers hand the files over only after it returns.
    /// </remarks>
    private async Task PlayAddFilesAcceptedAsync()
    {
        if (AddFilesOverlay.Visibility != Visibility.Visible) return;
        if (!CardMotion.AnimationsEnabled)
        {
            CloseAddFilesOverlay();
            return;
        }
        var generation = _addFilesOverlayGeneration;

        await AddFilesOverlayContent.SealBoxAsync();
        if (!IsAddFilesOverlayStillOpen(generation)) return;

        await PlayAddFilesCardCollapseAsync();
        if (!IsAddFilesOverlayStillOpen(generation)) return;
        CloseAddFilesOverlay();
    }

    private bool IsAddFilesOverlayStillOpen(int generation)
        => AddFilesOverlay.Visibility == Visibility.Visible && generation == _addFilesOverlayGeneration;

    // Stop() never raises Completed, so the collapse's awaiter is released here —
    // otherwise an Escape mid-collapse would strand the files being imported.
    private void StopAddFilesCardCollapse()
    {
        _addFilesCardCollapse?.Stop();
        _addFilesCardCollapse = null;
        _addFilesCardCollapseCompletion?.TrySetResult();
        _addFilesCardCollapseCompletion = null;
    }

    private Task PlayAddFilesCardCollapseAsync()
    {
        var cardCentre = AddFilesOverlayCard.TransformToVisual(AddFilesOverlay).TransformPoint(
            new Windows.Foundation.Point(AddFilesOverlayCard.ActualWidth / 2, AddFilesOverlayCard.ActualHeight / 2));
        var buttonCentre = AddFilesButton.TransformToVisual(AddFilesOverlay).TransformPoint(
            new Windows.Foundation.Point(AddFilesButton.ActualWidth / 2, AddFilesButton.ActualHeight / 2));

        // Scaling about the card's own centre keeps the shrink aimed at the button.
        _addFilesCardTransform.CenterX = AddFilesOverlayCard.ActualWidth / 2;
        _addFilesCardTransform.CenterY = AddFilesOverlayCard.ActualHeight / 2;

        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var storyboard = new Storyboard();
        storyboard.Children.Add(BuildCardAnimation("TranslateX", 0, buttonCentre.X - cardCentre.X, 300, easing));
        storyboard.Children.Add(BuildCardAnimation("TranslateY", 0, buttonCentre.Y - cardCentre.Y, 300, easing));
        storyboard.Children.Add(BuildCardAnimation("ScaleX", 1, 0.12, 300, easing));
        storyboard.Children.Add(BuildCardAnimation("ScaleY", 1, 0.12, 300, easing));
        storyboard.Children.Add(BuildCardOpacityAnimation(1, 0, 300));

        var completion = new TaskCompletionSource();
        storyboard.Completed += (_, _) => completion.TrySetResult();
        _addFilesCardCollapse = storyboard;
        _addFilesCardCollapseCompletion = completion;
        storyboard.Begin();
        return completion.Task;
    }

    // Lines the card's left edge up with the button that opened it. The button
    // lives in the row below the overlay, so only its horizontal position
    // carries over; the card's own Bottom alignment puts it just above the row.
    private void PositionAddFilesCard()
    {
        if (AddFilesOverlay.Visibility != Visibility.Visible) return;
        var left = AddFilesButton.TransformToVisual(AddFilesOverlay)
            .TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        var maxLeft = AddFilesOverlay.ActualWidth - AddFilesOverlayCard.ActualWidth;
        var margin = AddFilesOverlayCard.Margin;
        margin.Left = Math.Max(0, Math.Min(left, maxLeft));
        AddFilesOverlayCard.Margin = margin;
    }

    private void AddFilesOverlay_SizeChanged(object sender, SizeChangedEventArgs e) => PositionAddFilesCard();

    private void AddFilesOverlayCard_SizeChanged(object sender, SizeChangedEventArgs e) => PositionAddFilesCard();

    private void CloseAddFilesOverlay()
    {
        if (AddFilesOverlay.Visibility == Visibility.Collapsed) return;
        _addFilesCardEntrance?.Stop();
        StopAddFilesCardCollapse();
        AddFilesOverlay.Visibility = Visibility.Collapsed;
        AddFilesButton.Focus(FocusState.Programmatic);
    }

    private void AddFilesOverlay_Tapped(object sender, TappedRoutedEventArgs e) => CloseAddFilesOverlay();

    // Keeps a tap on the card itself from being read as a tap on the backdrop.
    private void AddFilesOverlayCard_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void AddFilesOverlayEscape_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        CloseAddFilesOverlay();
        args.Handled = true;
    }

    private void PickFiles_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.PickFiles", async () =>
        {
            if (ViewModel.IsImporting) return;
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            picker.FileTypeFilter.Add(".png");
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                Microsoft.UI.Win32Interop.GetWindowFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId));
            var files = await picker.PickMultipleFilesAsync();
            var paths = files.Select(f => f.Path).ToArray();
            if (paths.Length == 0) return; // Cancelled picker leaves the overlay open.
            await PlayAddFilesAcceptedAsync();
            if (await EnsureRequiredCookieSetupAsync(paths))
                await ViewModel.AddFilesCommand.ExecuteAsync(paths);
        });

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Import";
            e.DragUIOverride.IsCaptionVisible = true;
        }
    }

    private void Page_Drop(object sender, DragEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.Drop", async () =>
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
                // Returned rather than appended from the background thread, and
                // unreadable subdirectories are skipped so one of them cannot
                // discard everything already enumerated.
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
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<IAppLogger>()
                            .LogError("Import.EnumerateDroppedFolder", ex, folderPath);
                        return [];
                    }
                }));
            }
        }

        if (paths.Count == 0) return;
        await PlayAddFilesAcceptedAsync();
        if (await EnsureRequiredCookieSetupAsync(paths))
            await ViewModel.AddFilesCommand.ExecuteAsync(paths);
        });

    private void ReviewListHost_SizeChanged(object sender, SizeChangedEventArgs e)
        => ViewModel.SetReviewViewportWidth(e.NewSize.Width);

    private void ImportPage_Loaded(object sender, RoutedEventArgs e)
        => UpdateCompactLayout(ActualWidth, ActualHeight);

    private void ImportPage_SizeChanged(object sender, SizeChangedEventArgs e)
        => UpdateCompactLayout(e.NewSize.Width, e.NewSize.Height);

    private void UpdateCompactLayout(double width, double height)
    {
        var compact = width < 760;
        if (ReviewJumpPanel is not null)
            ReviewJumpPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        if (ImportFullActionText is not null && ImportCompactActionText is not null)
        {
            ImportFullActionText.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            ImportCompactActionText.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }
        if (ReviewEditScrollViewer is not null)
            ReviewEditScrollViewer.MaxHeight = Math.Max(180, Math.Min(440, height - 140));
        if (ReviewEditPanel is not null)
            ReviewEditPanel.Width = Math.Max(180, Math.Min(320, width - 80));
    }

    private void JumpToReviewSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string section }) return;
        var row = ViewModel.ReviewRows.FirstOrDefault(r => r.Key == section + ":");
        if (row is not null) ReviewListHost.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
    }

    private void SelectMixedReviewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ImportReviewRow row }) ViewModel.ToggleReviewRow(row);
    }

    private void SelectReviewSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ImportReviewRow row }) ViewModel.ToggleReviewSection(row);
    }

    private void SelectReviewGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ImportReviewGroup group }) ViewModel.ToggleReviewGroup(group);
    }

    private void ReviewArtworkInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || !ViewModel.IsReviewWorkspaceEnabled) return;
        e.Handled = true;
        UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.Lookup",
            () => ViewModel.FetchReviewArtworkCommand.ExecuteAsync(null));
    }

    private bool _pickReviewAuthor;
    private void PickReviewAuthor_Click(object sender, RoutedEventArgs e)
    {
        _pickTarget = null;
        _pickTargetUnknownGroup = null;
        _pickBatchUnknownAuthor = false;
        _pickBatchFetchFailedAuthor = false;
        _pickReviewAuthor = true;
        ReviewEditFlyout.Hide();
        ShowAuthorPickerFlyout(ReviewEditButton);
    }

    private void SearchSelected_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.SearchSelected", async () =>
        {
            var selected = ViewModel.SelectedVisibleReviewItems;
            if (selected.Count == 0 || !ViewModel.IsReviewWorkspaceEnabled) return;
            ReviewEditFlyout.Hide();
            var context = string.Format(ResLoader.GetString("Import_ReverseSelectionContext"), selected[0].FileName, selected.Count);
            var confirmation = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = ResLoader.GetString("Import_ReverseConfirmTitle"),
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new Image { Source = new BitmapImage(selected[0].ThumbnailUri), Height = 150, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform },
                        new TextBlock { Text = context, TextWrapping = TextWrapping.Wrap }
                    }
                },
                PrimaryButtonText = ResLoader.GetString("Import_ReverseStart"),
                CloseButtonText = ResLoader.GetString("Import_SauceNaoCancelButton"),
                DefaultButton = ContentDialogButton.Primary
            };
            if (await confirmation.ShowAsync() != ContentDialogResult.Primary) return;
            ViewModel.IsResolvingReview = true;
            ViewModel.ReviewBatchStatusText = ResLoader.GetString("Import_Review_Working");
            try
            {
                var result = await ViewModel.SearchSauceNaoForFilesAsync(selected, CancellationToken.None);
                if (result is null)
                { await ShowMessageDialog(ResLoader.GetString("Import_SauceNaoNoResultTitle"), ResLoader.GetString("Import_SauceNaoNoResultMessage")); return; }
                if (await ShowSauceNaoResultDialog(result, context) is { } rating)
                {
                    await ViewModel.ApplySearchResultToSelectionAsync(selected, result, rating);
                    ViewModel.ReviewBatchStatusText = ResLoader.GetString("Import_Review_BatchApplied");
                }
                else ViewModel.ReviewBatchStatusText = string.Empty;
            }
            catch (InvalidOperationException)
            { await ShowMessageDialog(ResLoader.GetString("Import_SauceNaoApiKeyMissingTitle"), ResLoader.GetString("Import_SauceNaoApiKeyMissingMessage")); }
            finally
            {
                ViewModel.IsResolvingReview = false;
                if (ViewModel.ReviewBatchStatusText == ResLoader.GetString("Import_Review_Working"))
                    ViewModel.ReviewBatchStatusText = string.Empty;
            }
        });

    private void AssignAuthor_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AssignAuthor", async () =>
        {
        if (sender is Button { CommandParameter: ImportArtworkGroup group })
            await ViewModel.AssignAuthorCommand.ExecuteAsync(group);
        });

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

    private void AuthorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.SelectAuthor", async () =>
        {
        if (sender is not ListView || e.AddedItems.Count == 0 || e.AddedItems[0] is not SelectableAuthor author) return;

        _authorFlyout?.Hide();

        if (_pickReviewAuthor)
        {
            _pickReviewAuthor = false;
            ViewModel.ReviewAuthorId = author.Id;
            ViewModel.ReviewAuthorProviderId = author.ProviderId;
            await ViewModel.ApplyReviewAuthorCommand.ExecuteAsync(null);
        }
        else if (_pickTarget is not null)
        {
            _pickTarget.ManualAuthorId = author.Id;
            _pickTarget.ManualAuthorProviderId = author.ProviderId;
            await ViewModel.AssignAuthorCommand.ExecuteAsync(_pickTarget);
            _pickTarget = null;
        }
        else if (_pickTargetUnknownGroup is not null)
        {
            _pickTargetUnknownGroup.ManualAuthorId = author.Id;
            _pickTargetUnknownGroup.ManualAuthorProviderId = author.ProviderId;
            await ViewModel.AssignAuthorToUnknownGroupCommand.ExecuteAsync(_pickTargetUnknownGroup);
            _pickTargetUnknownGroup = null;
        }
        else if (_pickBatchUnknownAuthor)
        {
            ViewModel.BatchManualAuthorId = author.Id;
            ViewModel.BatchManualAuthorProviderId = author.ProviderId;
            await ViewModel.AssignBatchAuthorIdToUnknownCommand.ExecuteAsync(null);
            _pickBatchUnknownAuthor = false;
        }
        else if (_pickBatchFetchFailedAuthor)
        {
            ViewModel.BatchFetchFailedAuthorId = author.Id;
            ViewModel.BatchFetchFailedAuthorProviderId = author.ProviderId;
            await ViewModel.AssignBatchAuthorIdToFetchFailedCommand.ExecuteAsync(null);
            _pickBatchFetchFailedAuthor = false;
        }
        });

    private void AssignAuthorToUnknownGroup_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AssignUnknownAuthor", async () =>
        {
        if (sender is Button { CommandParameter: ImportUnknownGroup group })
            await ViewModel.AssignAuthorToUnknownGroupCommand.ExecuteAsync(group);
        });

    private void AssignArtworkIdToUnknownGroup_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.AssignUnknownArtwork", async () =>
        {
        if (sender is Button { CommandParameter: ImportUnknownGroup group })
            await ViewModel.AssignArtworkIdToUnknownGroupCommand.ExecuteAsync(group);
        });

    private void SearchSauceNaoForUnknown_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.SearchUnknownImage", async () =>
        {
        if (sender is not Button { CommandParameter: ImportUnknownGroup group } button)
            return;

        button.IsEnabled = false;
        group.IsSauceNaoSearching = true;
        try
        {
            var result = await ViewModel.SearchSauceNaoForUnknownGroupAsync(group, CancellationToken.None);
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

            await ViewModel.ApplySauceNaoResultToUnknownGroupAsync(group, result, rating.Value);
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
        });

    private void SearchSauceNaoForFetchFailed_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.SearchFailedImage", async () =>
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
        });

    private async Task<ContentRating?> ShowSauceNaoResultDialog(ReverseImageSearchResult result, string? selectionContext = null)
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
        if (selectionContext is not null)
            panel.Children.Add(new TextBlock { Text = selectionContext, TextWrapping = TextWrapping.Wrap });
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

    private void SetBatchRatingFetchFailed_AllAges(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForFetchFailedCommand.Execute(ContentRating.AllAges);

    private void SetBatchRatingFetchFailed_R18(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForFetchFailedCommand.Execute(ContentRating.R18);

    private void SetBatchRatingFetchFailed_R18G(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForFetchFailedCommand.Execute(ContentRating.R18G);

    private void SetBatchRatingUnknown_AllAges(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForUnknownCommand.Execute(ContentRating.AllAges);

    private void SetBatchRatingUnknown_R18(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForUnknownCommand.Execute(ContentRating.R18);

    private void SetBatchRatingUnknown_R18G(object sender, RoutedEventArgs e) =>
        ViewModel.SetBatchRatingForUnknownCommand.Execute(ContentRating.R18G);

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

    private void CookieSetup_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "Import.CookieSetup", async () =>
        {
        var provider = _cookieSetupProviders.FirstOrDefault(p => p.NeedsCookieSetup)
            ?? _cookieSetupProviders.FirstOrDefault();
        if (provider is null) return;

        await ShowCookieSetupDialogAsync(provider);
        });

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
            ResLoader.GetString("Import_SauceNaoCloseButton"),
            App.Services.GetRequiredService<IAppLogger>());
}

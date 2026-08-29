using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using KoikatsuSceneGallery.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.UI.ViewManagement;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.UI.Composition;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.Storage.Pickers;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class ImportPage : Page
{
    private static readonly ResourceLoader ResLoader = new();
    private static readonly TimeSpan AnalysisIdAnimationDuration = TimeSpan.FromMilliseconds(240);
    private const float AnalysisIdAnimationOffset = 16f;
    private const double AnalysisIdMaxWidth = 136;
    private const double AnalysisIdViewportHeight = 20;

    public ImportViewModel ViewModel { get; }

    private readonly IReadOnlyList<ICookieSetupProvider> _cookieSetupProviders;
    private readonly IAppLogger _logger;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly UISettings _uiSettings = new();
    private bool _showingAnalysisIdA = true;
    private bool _isPreparingAnalysisId;
    private bool _isAnimatingAnalysisId;
    private bool _hasPendingAnalysisId;
    private string? _displayedAnalysisId;
    private string? _pendingAnalysisId;

    public ImportPage()
    {
        ViewModel = App.Services.GetService<ImportViewModel>()!;
        _logger = App.Services.GetRequiredService<IAppLogger>();
        _settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        _cookieSetupProviders = App.Services.GetRequiredService<PluginService>().CookieSetupProviders;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        NavigationCacheMode = NavigationCacheMode.Required;
        SetAnalyzingIdImmediately(ViewModel.CurrentAnalyzingArtworkId);

        if (_cookieSetupProviders.Count > 0)
            CookieSetupButton.Visibility = Visibility.Visible;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportViewModel.ShowWarningBar) && ViewModel.ShowWarningBar)
        {
            var messageKey = ViewModel.WarningKind switch
            {
                ImportWarningKind.ManualArtworkIdFetchFailed =>
                    "Import_ManualArtworkIdWarningMessage",
                _ => "Import_RejectedWarningMessage",
            };
            RejectedWarningBar.Message = string.Format(
                ResLoader.GetString(messageKey),
                ViewModel.WarningCount);
        }
        else if (e.PropertyName == nameof(ImportViewModel.CurrentAnalyzingArtworkId))
        {
            QueueAnalyzingIdTransition(ViewModel.CurrentAnalyzingArtworkId);
        }
    }

    private void QueueAnalyzingIdTransition(string? artworkId)
    {
        if (!_isAnimatingAnalysisId
            && string.Equals(_displayedAnalysisId, artworkId, StringComparison.Ordinal))
        {
            return;
        }

        _pendingAnalysisId = artworkId;
        _hasPendingAnalysisId = true;
        StartNextAnalyzingIdTransition();
    }

    private void StartNextAnalyzingIdTransition()
    {
        if (_isPreparingAnalysisId || _isAnimatingAnalysisId || !_hasPendingAnalysisId)
            return;

        var nextId = _pendingAnalysisId;
        _pendingAnalysisId = null;
        _hasPendingAnalysisId = false;

        if (string.Equals(_displayedAnalysisId, nextId, StringComparison.Ordinal))
            return;

        if (!IsLoaded || !_uiSettings.AnimationsEnabled)
        {
            SetAnalyzingIdImmediately(nextId);
            StartNextAnalyzingIdTransition();
            return;
        }

        var outgoing = _showingAnalysisIdA ? AnalyzingIdTextA : AnalyzingIdTextB;
        var incoming = _showingAnalysisIdA ? AnalyzingIdTextB : AnalyzingIdTextA;
        incoming.Text = nextId ?? string.Empty;

        AnalyzingIdViewport.Visibility = Visibility.Visible;
        var outgoingWidth = MeasureAnalyzingIdWidth(outgoing);
        var incomingWidth = MeasureAnalyzingIdWidth(incoming);
        SetAnalyzingIdViewportWidth(Math.Max(outgoingWidth, incomingWidth));
        _isPreparingAnalysisId = true;

        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _isPreparingAnalysisId = false;
            if (!IsLoaded || !_uiSettings.AnimationsEnabled)
            {
                SetAnalyzingIdImmediately(nextId);
                StartNextAnalyzingIdTransition();
                return;
            }

            try
            {
                AnimateAnalyzingIdTransition(
                    outgoing,
                    incoming,
                    nextId is not null,
                    incomingWidth);
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
                _logger.LogError("Import.AnimateArtworkId", ex);
                SetAnalyzingIdImmediately(nextId);
                StartNextAnalyzingIdTransition();
            }
        }))
        {
            _isPreparingAnalysisId = false;
            SetAnalyzingIdImmediately(nextId);
            StartNextAnalyzingIdTransition();
        }
    }

    private void AnimateAnalyzingIdTransition(
        TextBlock outgoing,
        TextBlock incoming,
        bool hasIncomingId,
        double incomingWidth)
    {
        var outgoingVisual = ElementCompositionPreview.GetElementVisual(outgoing);
        var incomingVisual = ElementCompositionPreview.GetElementVisual(incoming);
        var compositor = outgoingVisual.Compositor;
        var easing = compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.4f, 0f),
            new Vector2(0.2f, 1f));

        outgoingVisual.Offset = Vector3.Zero;
        outgoingVisual.Opacity = string.IsNullOrEmpty(outgoing.Text) ? 0f : 1f;
        incomingVisual.Offset = new Vector3(0, -AnalysisIdAnimationOffset, 0);
        incomingVisual.Opacity = 0f;

        var outgoingSlide = compositor.CreateVector3KeyFrameAnimation();
        outgoingSlide.InsertKeyFrame(1f, new Vector3(0, AnalysisIdAnimationOffset, 0), easing);
        outgoingSlide.Duration = AnalysisIdAnimationDuration;

        var outgoingFade = compositor.CreateScalarKeyFrameAnimation();
        outgoingFade.InsertKeyFrame(1f, 0f, easing);
        outgoingFade.Duration = AnalysisIdAnimationDuration;

        var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        outgoingVisual.StartAnimation("Offset", outgoingSlide);
        outgoingVisual.StartAnimation("Opacity", outgoingFade);

        if (hasIncomingId)
        {
            var incomingSlide = compositor.CreateVector3KeyFrameAnimation();
            incomingSlide.InsertKeyFrame(1f, Vector3.Zero, easing);
            incomingSlide.Duration = AnalysisIdAnimationDuration;

            var incomingFade = compositor.CreateScalarKeyFrameAnimation();
            incomingFade.InsertKeyFrame(1f, 1f, easing);
            incomingFade.Duration = AnalysisIdAnimationDuration;

            incomingVisual.StartAnimation("Offset", incomingSlide);
            incomingVisual.StartAnimation("Opacity", incomingFade);
        }

        _isAnimatingAnalysisId = true;
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            outgoing.Text = string.Empty;
            ResetAnalyzingIdVisual(outgoingVisual, 0f);
            ResetAnalyzingIdVisual(incomingVisual, hasIncomingId ? 1f : 0f);
            _showingAnalysisIdA = !_showingAnalysisIdA;
            _displayedAnalysisId = hasIncomingId ? incoming.Text : null;
            _isAnimatingAnalysisId = false;
            SetAnalyzingIdViewportWidth(hasIncomingId ? incomingWidth : 0);
            AnalyzingIdViewport.Visibility = hasIncomingId
                ? Visibility.Visible
                : Visibility.Collapsed;
            StartNextAnalyzingIdTransition();
        });
        batch.End();
    }

    private void SetAnalyzingIdImmediately(string? artworkId)
    {
        AnalyzingIdTextA.Text = artworkId ?? string.Empty;
        AnalyzingIdTextB.Text = string.Empty;
        var width = artworkId is null ? 0 : MeasureAnalyzingIdWidth(AnalyzingIdTextA);
        SetAnalyzingIdViewportWidth(width);
        AnalyzingIdViewport.Visibility = artworkId is null
            ? Visibility.Collapsed
            : Visibility.Visible;

        try
        {
            ResetAnalyzingIdVisual(
                ElementCompositionPreview.GetElementVisual(AnalyzingIdTextA),
                artworkId is null ? 0f : 1f);
            ResetAnalyzingIdVisual(
                ElementCompositionPreview.GetElementVisual(AnalyzingIdTextB),
                0f);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            _logger.LogError("Import.ResetArtworkIdAnimation", ex);
            AnalyzingIdTextA.Opacity = artworkId is null ? 0 : 1;
            AnalyzingIdTextB.Opacity = 0;
        }

        _showingAnalysisIdA = true;
        _displayedAnalysisId = artworkId;
        _isPreparingAnalysisId = false;
        _isAnimatingAnalysisId = false;
    }

    private static double MeasureAnalyzingIdWidth(TextBlock textBlock)
    {
        textBlock.Measure(new Size(double.PositiveInfinity, AnalysisIdViewportHeight));
        return Math.Clamp(
            Math.Ceiling(textBlock.DesiredSize.Width),
            0,
            AnalysisIdMaxWidth);
    }

    private void SetAnalyzingIdViewportWidth(double width)
    {
        var constrainedWidth = Math.Clamp(width, 0, AnalysisIdMaxWidth);
        AnalyzingIdViewport.Width = constrainedWidth;
        AnalyzingIdClip.Rect = new Rect(
            0,
            0,
            constrainedWidth,
            AnalysisIdViewportHeight);
    }

    private static void ResetAnalyzingIdVisual(Visual visual, float opacity)
    {
        visual.StopAnimation("Offset");
        visual.StopAnimation("Opacity");
        visual.Offset = Vector3.Zero;
        visual.Opacity = opacity;
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

    private void Page_Drop(object sender, DragEventArgs e)
        => UiEventGuard.Run(_logger, "Import.Drop", async () =>
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
                paths.AddRange(await EnumeratePngFilesAsync(folder.Path, "Import.EnumerateDroppedFolder"));
            }
        }

        await ImportPathsAsync(paths);
        });

    private void PickSource_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(_logger, "Import.PickSource", async () =>
        {
            if (sender is not DropDownButton button
                || !await EnsureImportPickerExplanationAsync())
            {
                return;
            }

            if (Resources["ImportSourceFlyout"] is MenuFlyout flyout)
                flyout.ShowAt(button);
        });

    private void PickFiles_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(_logger, "Import.PickFiles", async () =>
        {
            var picker = new FileOpenPicker(XamlRoot.ContentIslandEnvironment.AppWindowId);
            picker.FileTypeFilter.Add(".png");
            var files = await picker.PickMultipleFilesAsync();
            var paths = files
                .Select(file => file.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();

            await ImportPathsAsync(paths);
        });

    private void PickFolder_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(_logger, "Import.PickFolder", async () =>
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(
                XamlRoot.ContentIslandEnvironment.AppWindowId);
            var folder = await picker.PickSingleFolderAsync();
            if (folder is null || string.IsNullOrWhiteSpace(folder.Path))
                return;

            var paths = await EnumeratePngFilesAsync(
                folder.Path,
                "Import.EnumeratePickedFolder");
            await ImportPathsAsync(paths);
        });

    private async Task<bool> EnsureImportPickerExplanationAsync()
    {
        if (_settingsViewModel.ImportPickerExplanationShown)
            return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("Import_PickerExplanationTitle"),
            Content = new TextBlock
            {
                Text = ResLoader.GetString("Import_PickerExplanationMessage"),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440,
            },
            PrimaryButtonText = ResLoader.GetString("Import_PickerExplanationContinue"),
            CloseButtonText = ResLoader.GetString("Import_PickerExplanationCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return false;

        await _settingsViewModel.MarkImportPickerExplanationShownAsync();
        return true;
    }

    private async Task<IReadOnlyList<string>> EnumeratePngFilesAsync(
        string folderPath,
        string operation)
    {
        return await Task.Run<IReadOnlyList<string>>(() =>
        {
            try
            {
                return Directory
                    .EnumerateFiles(folderPath, "*.png", SearchOption.AllDirectories)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(operation, ex, folderPath);
                return [];
            }
        });
    }

    private async Task ImportPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0 && await EnsureRequiredCookieSetupAsync(paths))
            await ViewModel.AddFilesCommand.ExecuteAsync(paths);
    }

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

        if (_pickTarget is not null)
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

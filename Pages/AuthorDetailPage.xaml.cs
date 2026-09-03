using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed partial class AuthorDetailPage : Page
{
    private static readonly ResourceLoader ResLoader = new();

    public AuthorDetailViewModel ViewModel { get; } = new(
        App.Services.GetService<AuthorPostService>(),
        App.Services.GetRequiredService<GalleryViewModel>(),
        App.Services.GetRequiredService<CharacterGalleryViewModel>(),
        App.Services.GetRequiredService<CoordinateGalleryViewModel>(),
        App.Services.GetRequiredService<IAppLogger>());

    private const double SceneImageRatio = 135.0 / 240.0;
    private const double CharaImageRatio = 352.0 / 252.0;
    private const double PostImageRatio = 3.0 / 4.0;
    private const double CardMargin = 4;
    private const double CardInset = 4 + 1;
    private const double CellOverheadW = CardMargin * 2;
    private const double ContentInsetW = (CardMargin + CardInset) * 2;
    private const double FilenameReserve = 30;
    private const double DesiredWidth = 240;
    private const double PostDesiredWidth = 160;
    private const double PostItemSpacing = 8;
    private const int OverviewTabIndex = 0;
    private const int ScenesTabIndex = 1;
    private const int CharactersTabIndex = 2;
    private const int CoordinatesTabIndex = 3;
    private const int PostsTabIndex = 4;

    private CancellationTokenSource? _postsCts;
    private AuthorDetailNavigationParameter? _navigationParameter;
    private readonly HashSet<SceneCard> _requestedSceneThumbnails = [];
    private readonly HashSet<CharacterCard> _requestedCharacterThumbnails = [];
    private readonly HashSet<CoordinateCard> _requestedCoordinateThumbnails = [];

    public AuthorDetailPage()
    {
        InitializeComponent();
        // This page is parameterized by author. Reusing one required cache entry
        // for every author lets Pivot retain visual state from the previous
        // author, so keep the visual tree scoped to one navigation entry.
        NavigationCacheMode = NavigationCacheMode.Disabled;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (TryGetNavigationParameter(e.Parameter, out var navigationParameter))
        {
            var restoringSameAuthor = e.NavigationMode == NavigationMode.Back
                && ViewModel.Author?.Key == navigationParameter.Summary.Display.Key;
            _navigationParameter = navigationParameter;
            if (!restoringSameAuthor)
            {
                ViewModel.Load(navigationParameter.Summary);
                UpdateTabPresence();
                SelectTab(OverviewTabIndex);
                RequestOverviewThumbnails();
            }
            else
            {
                UpdateTabPresence();
            }
            RestoreSelectedTab(e.NavigationMode);
            if (ViewModel.CanLoadPosts
                && (!restoringSameAuthor
                    || (ViewModel.Posts.Count == 0 && ViewModel.UnassignedImages.Count == 0))
                && App.Services.GetService<AuthorPostService>() is { } postService)
            {
                _postsCts = new CancellationTokenSource();
                ViewModel.LoadPostsAsync(postService, _postsCts.Token)
                    .Observe(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.LoadPosts");
            }
        }

        ScenesGrid.SizeChanged += Grid_SizeChanged;
        CharactersGrid.SizeChanged += Grid_SizeChanged;
        CoordinatesGrid.SizeChanged += Grid_SizeChanged;

        DispatcherQueue.TryEnqueue(() =>
        {
            ViewModel.SetOverviewPreviewWidth(OverviewContent.ActualWidth);
            RequestOverviewThumbnails();
            ApplyLayout(ScenesGrid, SceneImageRatio);
            ApplyLayout(CharactersGrid, CharaImageRatio);
            ApplyLayout(CoordinatesGrid, CharaImageRatio);
        });
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ReleaseRequestedThumbnails();
        _postsCts?.Cancel();
        _postsCts?.Dispose();
        _postsCts = null;
        ScenesGrid.SizeChanged -= Grid_SizeChanged;
        CharactersGrid.SizeChanged -= Grid_SizeChanged;
        CoordinatesGrid.SizeChanged -= Grid_SizeChanged;
    }

    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
            ApplyLayout(grid, grid == ScenesGrid ? SceneImageRatio : CharaImageRatio);
    }

    private void OverviewContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ViewModel.SetOverviewPreviewWidth(e.NewSize.Width);
        RequestOverviewThumbnails();
    }

    private void RequestOverviewThumbnails()
    {
        var scenes = App.Services.GetRequiredService<GalleryViewModel>();
        var characters = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        var coordinates = App.Services.GetRequiredService<CoordinateGalleryViewModel>();
        scenes.ActivateThumbnailRequests();
        characters.ActivateThumbnailRequests();
        coordinates.ActivateThumbnailRequests();

        foreach (var card in ViewModel.ScenePreviews)
            RequestThumbnail(card);
        foreach (var card in ViewModel.CharacterPreviews)
            RequestThumbnail(card);
        foreach (var card in ViewModel.CoordinatePreviews)
            RequestThumbnail(card);
    }

    private void ScenesGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is SceneCard card)
                ReleaseThumbnail(card);
            return;
        }

        args.RegisterUpdateCallback(ScenesGrid_Phase1);
    }

    private void ScenesGrid_Phase1(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is SceneCard card)
            RequestThumbnail(card);
    }

    private void CharactersGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is CharacterCard card)
                ReleaseThumbnail(card);
            return;
        }

        args.RegisterUpdateCallback(CharactersGrid_Phase1);
    }

    private void CharactersGrid_Phase1(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is CharacterCard card)
            RequestThumbnail(card);
    }

    private void CoordinatesGrid_ContainerContentChanging(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue)
        {
            if (args.Item is CoordinateCard card)
                ReleaseThumbnail(card);
            return;
        }

        args.RegisterUpdateCallback(CoordinatesGrid_Phase1);
    }

    private void CoordinatesGrid_Phase1(
        ListViewBase sender,
        ContainerContentChangingEventArgs args)
    {
        if (args.Item is CoordinateCard card)
            RequestThumbnail(card);
    }

    private void RequestThumbnail(SceneCard card)
    {
        _requestedSceneThumbnails.Add(card);
        App.Services.GetRequiredService<GalleryViewModel>()
            .RequestThumbnail(card, ThumbnailWorkPriority.Visible);
    }

    private void RequestThumbnail(CharacterCard card)
    {
        _requestedCharacterThumbnails.Add(card);
        App.Services.GetRequiredService<CharacterGalleryViewModel>()
            .RequestThumbnail(card, ThumbnailWorkPriority.Visible);
    }

    private void RequestThumbnail(CoordinateCard card)
    {
        _requestedCoordinateThumbnails.Add(card);
        App.Services.GetRequiredService<CoordinateGalleryViewModel>()
            .RequestThumbnail(card, ThumbnailWorkPriority.Visible);
    }

    private void ReleaseThumbnail(SceneCard card)
    {
        _requestedSceneThumbnails.Remove(card);
        App.Services.GetRequiredService<GalleryViewModel>().ReleaseThumbnail(card);
    }

    private void ReleaseThumbnail(CharacterCard card)
    {
        _requestedCharacterThumbnails.Remove(card);
        App.Services.GetRequiredService<CharacterGalleryViewModel>().ReleaseThumbnail(card);
    }

    private void ReleaseThumbnail(CoordinateCard card)
    {
        _requestedCoordinateThumbnails.Remove(card);
        App.Services.GetRequiredService<CoordinateGalleryViewModel>().ReleaseThumbnail(card);
    }

    private void ReleaseRequestedThumbnails()
    {
        var sceneViewModel = App.Services.GetRequiredService<GalleryViewModel>();
        foreach (var card in _requestedSceneThumbnails)
            sceneViewModel.ReleaseThumbnail(card);
        _requestedSceneThumbnails.Clear();

        var characterViewModel = App.Services.GetRequiredService<CharacterGalleryViewModel>();
        foreach (var card in _requestedCharacterThumbnails)
            characterViewModel.ReleaseThumbnail(card);
        _requestedCharacterThumbnails.Clear();

        var coordinateViewModel = App.Services.GetRequiredService<CoordinateGalleryViewModel>();
        foreach (var card in _requestedCoordinateThumbnails)
            coordinateViewModel.ReleaseThumbnail(card);
        _requestedCoordinateThumbnails.Clear();
    }

    private static void ApplyLayout(GridView grid, double imageRatio)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (DesiredWidth + CellOverheadW)));
        double cellW = (available / columns) - 0.5;
        double imageH = Math.Max(0, cellW - ContentInsetW) * imageRatio;
        double cellH = imageH + FilenameReserve + (CardMargin + CardInset) * 2;

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    private static void ApplyPostImageLayout(GridView grid)
    {
        if (grid.ItemsPanelRoot is not ItemsWrapGrid panel || panel.ActualWidth <= 0)
            return;

        double available = panel.ActualWidth;
        int columns = Math.Max(1, (int)Math.Floor(available / (PostDesiredWidth + PostItemSpacing)));
        double cellW = (available / columns) - PostItemSpacing;
        double cellH = cellW * PostImageRatio;

        panel.ItemWidth = cellW;
        panel.ItemHeight = cellH;
    }

    private void PostImagesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is GridView grid)
            ApplyPostImageLayout(grid);
    }

    public static string FormatCount(int count) => $"({count})";

    public static BitmapImage? CreateThumbnail(Uri? uri) =>
        uri is null ? null : new BitmapImage { DecodePixelWidth = 300, UriSource = uri };

    public static BitmapImage? CreatePostThumbnail(Uri? uri) =>
        uri is null ? null : new BitmapImage { DecodePixelWidth = 160, UriSource = uri };

    public static double GetPreviewWidth(int width, int height, double previewHeight) =>
        width > 0 && height > 0
            ? Math.Clamp(previewHeight * width / height, 72, 320)
            : previewHeight * 4.0 / 3.0;

    public static double GetPreviewWidth(double aspectRatio, double previewHeight) =>
        Math.Clamp(previewHeight * aspectRatio, 72, 320);

    public static string FormatFileCount(int count) => count == 1 ? "1 file" : $"{count} files";

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }

    private void OpenProfile_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.OpenProfile", async () =>
        {
            if (ViewModel.Author is { } author)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(author.ProfileUrl));
        });

    private void Shuffle_Click(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(TabPivot.SelectedItem, ScenesPivotItem) && ViewModel.Scenes.Count > 0)
        {
            SetRestoreSelectedTabOnBack(ScenesTabIndex);
            Frame.Navigate(typeof(DetailPage),
                CreateScopedParameter(ViewModel.Scenes[Random.Shared.Next(ViewModel.Scenes.Count)]));
        }
        else if (ReferenceEquals(TabPivot.SelectedItem, CharactersPivotItem) && ViewModel.Characters.Count > 0)
        {
            SetRestoreSelectedTabOnBack(CharactersTabIndex);
            Frame.Navigate(typeof(CharacterDetailPage),
                CreateScopedParameter(ViewModel.Characters[Random.Shared.Next(ViewModel.Characters.Count)]));
        }
        else if (ReferenceEquals(TabPivot.SelectedItem, CoordinatesPivotItem) && ViewModel.Coordinates.Count > 0)
        {
            SetRestoreSelectedTabOnBack(CoordinatesTabIndex);
            Frame.Navigate(typeof(CoordinateDetailPage),
                CreateScopedParameter(ViewModel.Coordinates[Random.Shared.Next(ViewModel.Coordinates.Count)]));
        }
        else if (ReferenceEquals(TabPivot.SelectedItem, PostsPivotItem) && ViewModel.Posts.Count > 0)
        {
            SetRestoreSelectedTabOnBack(PostsTabIndex);
            Frame.Navigate(typeof(PostDetailPage),
                CreatePostParameter(ViewModel.Posts[Random.Shared.Next(ViewModel.Posts.Count)]));
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.Refresh", async () =>
        {
            if (ViewModel.Author is { } author)
                await App.Services.GetRequiredService<AuthorInfoService>().RefreshAuthorAsync(author.Key);
        });

    private void ScenesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SceneCard card)
        {
            SetRestoreSelectedTabOnBack(ScenesTabIndex);
            Frame.Navigate(typeof(DetailPage), CreateScopedParameter(card));
        }
    }

    private void CharactersGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CharacterCard card)
        {
            SetRestoreSelectedTabOnBack(CharactersTabIndex);
            Frame.Navigate(typeof(CharacterDetailPage), CreateScopedParameter(card));
        }
    }

    private void CoordinatesGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is CoordinateCard card)
        {
            SetRestoreSelectedTabOnBack(CoordinatesTabIndex);
            Frame.Navigate(typeof(CoordinateDetailPage), CreateScopedParameter(card));
        }
    }

    private void PostTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AuthorPost post })
        {
            SetRestoreSelectedTabOnBack(PostsTabIndex);
            Frame.Navigate(typeof(PostDetailPage), CreatePostParameter(post));
        }
    }

    private void OverviewSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value }
            && int.TryParse(value, out var tabIndex))
        {
            SelectTab(tabIndex);
        }
    }

    private void OverviewPost_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PostImageGroupViewModel group)
        {
            SetRestoreSelectedTabOnBack(OverviewTabIndex);
            Frame.Navigate(typeof(PostDetailPage), CreatePostParameter(group.Post));
        }
    }

    private void PostsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PostImageGroupViewModel group)
        {
            SetRestoreSelectedTabOnBack(PostsTabIndex);
            Frame.Navigate(typeof(PostDetailPage), CreatePostParameter(group.Post));
        }
    }

    private void AssignUnclassified_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "AuthorDetail.AssignUnclassified", async () =>
        {
            if (App.Services.GetService<AuthorPostService>() is { } postService)
                await ShowAssignUnclassifiedDialogAsync(postService);
        });

    private async Task ShowAssignUnclassifiedDialogAsync(AuthorPostService postService)
    {
        if (ViewModel.UnassignedImages.Count == 0)
            return;

        var artworkIdBox = new TextBox
        {
            PlaceholderText = ResLoader.GetString("AuthorDetail_ArtworkIdPlaceholder"),
        };
        var selectedCount = new TextBlock
        { };
        var error = new TextBlock
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                Microsoft.UI.Colors.IndianRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
        };
        var progress = new ProgressRing
        {
            Width = 18,
            Height = 18,
            IsActive = false,
            Visibility = Visibility.Collapsed,
        };
        var files = new ListView
        {
            ItemsSource = ViewModel.UnassignedImages,
            SelectionMode = ListViewSelectionMode.Multiple,
            ItemTemplate = (DataTemplate)Resources["UnassignedAuthorImageTemplate"],
            MaxHeight = 360,
        };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = ResLoader.GetString("AuthorDetail_AssignUnclassifiedDescription"),
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(artworkIdBox);
        content.Children.Add(selectedCount);
        content.Children.Add(error);
        content.Children.Add(progress);
        content.Children.Add(files);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = ResLoader.GetString("AuthorDetail_AssignUnclassifiedTitle"),
            Content = content,
            PrimaryButtonText = ResLoader.GetString("AuthorDetail_Assign"),
            CloseButtonText = ResLoader.GetString("AuthorDetail_Done"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
        };

        var isAssigning = false;
        void UpdateState()
        {
            selectedCount.Text = string.Format(
                ResLoader.GetString("AuthorDetail_SelectedFiles"),
                files.SelectedItems.Count);
            dialog.IsPrimaryButtonEnabled = !isAssigning
                && files.SelectedItems.Count > 0
                && !string.IsNullOrWhiteSpace(artworkIdBox.Text);
        }

        artworkIdBox.TextChanged += (_, _) => UpdateState();
        files.SelectionChanged += (_, _) => UpdateState();
        UpdateState();

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            // Keep this batch workspace open after each successful assignment.
            args.Cancel = true;
            var deferral = args.GetDeferral();
            isAssigning = true;
            error.Visibility = Visibility.Collapsed;
            progress.IsActive = true;
            progress.Visibility = Visibility.Visible;
            UpdateState();
            try
            {
                var selected = files.SelectedItems.OfType<UnassignedAuthorImage>().ToList();
                using var operationCts = new CancellationTokenSource();
                await ViewModel.AssignUnassignedImagesAsync(
                    postService,
                    selected,
                    artworkIdBox.Text,
                    operationCts.Token);

                files.SelectedItems.Clear();
                artworkIdBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                App.Services.GetRequiredService<IAppLogger>()
                    .LogError("AuthorDetail.AssignUnclassified", ex, artworkIdBox.Text);
                error.Text = ex.Message;
                error.Visibility = Visibility.Visible;
            }
            finally
            {
                isAssigning = false;
                progress.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                UpdateState();
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private void PostImage_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LocalImagePreview preview) return;

        var path = preview.FilePath;
        var scene = App.Services.GetRequiredService<GalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (scene is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(DetailPage), CreateScopedParameter(scene)); return; }

        var character = App.Services.GetRequiredService<CharacterGalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (character is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(CharacterDetailPage), CreateScopedParameter(character)); return; }

        var coordinate = App.Services.GetRequiredService<CoordinateGalleryViewModel>().Cards.FirstOrDefault(c => c.FilePath == path);
        if (coordinate is not null) { SetRestoreSelectedTabOnBack(PostsTabIndex); Frame.Navigate(typeof(CoordinateDetailPage), CreateScopedParameter(coordinate)); return; }
    }

    private void PostImages_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<LocalImagePreview>().Select(p => p.FilePath));

    private void ScenesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<SceneCard>().Select(c => c.FilePath));

    private void CharactersGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<CharacterCard>().Select(c => c.FilePath));

    private void CoordinatesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => SetDragFiles(e, e.Items.OfType<CoordinateCard>().Select(c => c.FilePath));

    private static void SetDragFiles(DragItemsStartingEventArgs e, IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return;
        e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, async request =>
        {
            var deferral = request.GetDeferral();
            try
            {
                var files = new List<IStorageItem>();
                foreach (var path in paths)
                {
                    try { files.Add(await StorageFile.GetFileFromPathAsync(path)); }
                    catch (Exception ex)
                    {
                        App.Services.GetRequiredService<IAppLogger>()
                            .LogError("AuthorDetail.PrepareDragFile", ex, path);
                    }
                }
                request.SetData(files);
            }
            finally { deferral.Complete(); }
        });
    }

    private static bool TryGetNavigationParameter(object? parameter, out AuthorDetailNavigationParameter navigationParameter)
    {
        switch (parameter)
        {
            case AuthorDetailNavigationParameter value:
                navigationParameter = value;
                return true;
            case AuthorSummary summary:
                navigationParameter = new AuthorDetailNavigationParameter(summary);
                return true;
            default:
                navigationParameter = null!;
                return false;
        }
    }

    private void SetRestoreSelectedTabOnBack(int tabIndex)
    {
        if (_navigationParameter is not null)
            _navigationParameter.RestoreSelectedTabOnBack = tabIndex;
    }

    private object CreateScopedParameter(SceneCard card) => ViewModel.Author is { } author
        ? new AuthorScopedSceneNavigationParameter(card, author.Key)
        : card;

    private object CreateScopedParameter(CharacterCard card) => ViewModel.Author is { } author
        ? new AuthorScopedCharacterNavigationParameter(card, author.Key)
        : card;

    private object CreateScopedParameter(CoordinateCard card) => ViewModel.Author is { } author
        ? new AuthorScopedCoordinateNavigationParameter(card, author.Key)
        : card;

    private object CreatePostParameter(AuthorPost post) => ViewModel.Author is { } author
        ? new AuthorPostNavigationParameter(post, author.Key)
        : post;

    private void RestoreSelectedTab(NavigationMode navigationMode)
    {
        if (navigationMode == NavigationMode.Back && _navigationParameter?.RestoreSelectedTabOnBack is { } tabIndex)
            SelectTab(tabIndex);

        if (_navigationParameter is not null)
            _navigationParameter.RestoreSelectedTabOnBack = null;
    }

    private void UpdateTabPresence()
    {
        SetTabPresence(ScenesPivotItem, ViewModel.HasScenes);
        SetTabPresence(CharactersPivotItem, ViewModel.HasCharacters);
        SetTabPresence(CoordinatesPivotItem, ViewModel.HasCoordinates);
        SetTabPresence(PostsPivotItem, ViewModel.CanLoadPosts);
    }

    private void SetTabPresence(PivotItem tab, bool shouldShow)
    {
        var isPresent = TabPivot.Items.Contains(tab);
        if (!shouldShow && isPresent)
        {
            TabPivot.Items.Remove(tab);
        }
        else if (shouldShow && !isPresent)
        {
            var orderedTabs = new[]
            {
                OverviewPivotItem,
                ScenesPivotItem,
                CharactersPivotItem,
                CoordinatesPivotItem,
                PostsPivotItem,
            };
            var insertAt = 0;
            foreach (var candidate in orderedTabs)
            {
                if (ReferenceEquals(candidate, tab))
                    break;
                if (TabPivot.Items.Contains(candidate))
                    insertAt++;
            }
            TabPivot.Items.Insert(insertAt, tab);
        }
    }

    private void SelectTab(int logicalTabIndex)
    {
        var tab = logicalTabIndex switch
        {
            OverviewTabIndex => OverviewPivotItem,
            ScenesTabIndex => ScenesPivotItem,
            CharactersTabIndex => CharactersPivotItem,
            CoordinatesTabIndex => CoordinatesPivotItem,
            PostsTabIndex => PostsPivotItem,
            _ => OverviewPivotItem,
        };

        TabPivot.SelectedItem = TabPivot.Items.Contains(tab)
            ? tab
            : OverviewPivotItem;
    }

}

using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed record TagDisplay(string Display);

public sealed partial class PostDetailPage : Page
{
    public PostDetailViewModel ViewModel { get; } = new(
        App.Services.GetRequiredService<IAppLogger>());

    private CancellationTokenSource? _cts;
    private SceneGallery.PluginSdk.AuthorKey? _authorScope;

    public PostDetailPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
        ContentGrid.SizeChanged += (_, _) => UpdateLayoutForWidth();
        SizeChanged += (_, _) => UpdateLayoutForWidth();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var post = e.Parameter switch
        {
            AuthorPostNavigationParameter scoped => SetPostScope(scoped),
            AuthorPost direct => SetPostScope(direct),
            _ => null,
        };
        if (post is not null)
        {
            if (ViewModel.Post != post) ViewModel.Load(post);
            RenderDescription();
            if (!post.IsDetailLoaded && App.Services.GetService<AuthorPostService>() is { } postService)
            {
                _cts = new CancellationTokenSource();
                ViewModel.LoadDetailAsync(postService, _cts.Token)
                    .Observe(App.Services.GetRequiredService<IAppLogger>(), "PostDetail.Load");
            }
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }

    private void LocalImage_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is LocalImagePreview preview)
            ViewModel.SelectedImage = preview;
    }

    private void MainImage_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedImage is not { } preview) return;

        OpenLocalImage(preview);
    }

    private void OpenLocalImage(LocalImagePreview preview)
    {
        var live = App.Services.GetRequiredService<KoikatsuSceneGallery.Services.LibraryRegistry>().All.SelectMany(library => library.Cards)
            .DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);
        var cards = ViewModel.LocalImages.Where(p => live.ContainsKey(p.FilePath)).Select(p => live[p.FilePath]).ToArray();
        if (live.TryGetValue(preview.FilePath, out var selected))
            new BrowseContext(ViewModel.DisplayTitle, cards, true).Open(Frame, selected);
    }

    private void UpdateLayoutForWidth()
    {
        MainImageFrame.MaxHeight = Math.Clamp(ActualHeight - 270, 200, 640);
        bool wide = ContentGrid.ActualWidth >= 900 && ViewModel.HasLocalImages;
        ContentGrid.ColumnDefinitions[1].Width = wide ? new GridLength(340) : new GridLength(0);
        Grid.SetColumn(PostInfoPanel, wide ? 1 : 0);
        Grid.SetRow(PostInfoPanel, wide ? 0 : 1);
        ContentGrid.ColumnSpacing = wide ? 24 : 0;
    }

    public static BitmapImage? CreateMainImage(LocalImagePreview? preview) => preview is null
        ? null
        : new BitmapImage { DecodePixelWidth = 1200, UriSource = preview.ImageUri };

    private AuthorPost SetPostScope(AuthorPostNavigationParameter scoped)
    {
        _authorScope = scoped.AuthorKey;
        return scoped.Post;
    }

    private AuthorPost SetPostScope(AuthorPost direct)
    {
        _authorScope = null;
        return direct;
    }

    private object CreateScopedParameter(SceneCard card) => _authorScope is { } author
        ? new AuthorScopedSceneNavigationParameter(card, author)
        : card;

    private object CreateScopedParameter(CharacterCard card) => _authorScope is { } author
        ? new AuthorScopedCharacterNavigationParameter(card, author)
        : card;

    private object CreateScopedParameter(CoordinateCard card) => _authorScope is { } author
        ? new AuthorScopedCoordinateNavigationParameter(card, author)
        : card;

    private void OpenInBrowser_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "PostDetail.OpenBrowser", async () =>
        {
            if (ViewModel.Post is { } post)
                await Windows.System.Launcher.LaunchUriAsync(new Uri(post.ArtworkUrl));
        });

    private void Save_Click(object sender, RoutedEventArgs e)
        => UiEventGuard.Run(App.Services.GetRequiredService<IAppLogger>(), "PostDetail.Save", async () =>
        {
            if (App.Services.GetService<AuthorPostService>() is { } postService)
                await ViewModel.SaveToCacheAsync(postService, _cts?.Token ?? CancellationToken.None);
        });

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PostDetailViewModel.Description))
            RenderDescription();
        if (e.PropertyName == nameof(PostDetailViewModel.HasLocalImages)) UpdateLayoutForWidth();
    }

    private void LocalImagesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        => DragFilePayload.Attach(e, "PostDetail.Drag", e.Items.OfType<LocalImagePreview>().Select(p => p.FilePath));

    private void RenderDescription()
        => HtmlDescriptionRenderer.Render(DescriptionText, ViewModel.Description);
}

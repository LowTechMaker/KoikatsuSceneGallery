using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public sealed class PostImageGroupViewModel
{
    public AuthorPost Post { get; }
    public ObservableCollection<LocalImagePreview> Images { get; } = [];
    public LocalImagePreview? CoverImage => Images.FirstOrDefault();
    public Uri? CoverImageUri => CoverImage?.ImageUri;
    public double CoverAspectRatio => CoverImage?.AspectRatio ?? 4.0 / 3.0;
    public int LocalImageCount => Images.Count;
    public string AdditionalImageCountText => $"+{Math.Max(0, LocalImageCount - 1)}";
    public bool HasAdditionalImages => LocalImageCount > 1;

    public PostImageGroupViewModel(
        AuthorPost post,
        IReadOnlyDictionary<string, double>? imageAspectRatios = null)
    {
        Post = post;
        foreach (var path in post.LocalFilePaths.Where(File.Exists))
        {
            var aspectRatio = imageAspectRatios?.GetValueOrDefault(path) ?? 4.0 / 3.0;
            Images.Add(new LocalImagePreview(
                new Uri(path.Replace("#", "%23")),
                Path.GetFileName(path),
                path,
                aspectRatio));
        }
    }
}

public partial class AuthorDetailViewModel : ObservableObject
{
    private const double PreviewHeight = 132;
    private const double PreviewSpacing = 8;
    private double _overviewPreviewWidth = 800;
    private readonly AuthorPostService? _authorPostService;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly CharacterGalleryViewModel _characterGalleryViewModel;
    private readonly CoordinateGalleryViewModel _coordinateGalleryViewModel;
    private readonly IAppLogger _logger;

    public AuthorDetailViewModel(
        AuthorPostService? authorPostService,
        GalleryViewModel galleryViewModel,
        CharacterGalleryViewModel characterGalleryViewModel,
        CoordinateGalleryViewModel coordinateGalleryViewModel,
        IAppLogger logger)
    {
        _authorPostService = authorPostService;
        _galleryViewModel = galleryViewModel;
        _characterGalleryViewModel = characterGalleryViewModel;
        _coordinateGalleryViewModel = coordinateGalleryViewModel;
        _logger = logger;
    }

    [ObservableProperty]
    public partial AuthorDisplay? Author { get; set; }

    public ObservableCollection<SceneCard> Scenes { get; } = [];
    public ObservableCollection<CharacterCard> Characters { get; } = [];
    public ObservableCollection<CoordinateCard> Coordinates { get; } = [];
    public ObservableCollection<AuthorPost> Posts { get; } = [];
    public ObservableCollection<PostImageGroupViewModel> PostGroups { get; } = [];
    public ObservableCollection<UnassignedAuthorImage> UnassignedImages { get; } = [];
    public ObservableCollection<SceneCard> ScenePreviews { get; } = [];
    public ObservableCollection<CharacterCard> CharacterPreviews { get; } = [];
    public ObservableCollection<CoordinateCard> CoordinatePreviews { get; } = [];
    public ObservableCollection<PostImageGroupViewModel> PostPreviews { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasScenes))]
    public partial int SceneCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCharacters))]
    public partial int CharacterCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoordinates))]
    public partial int CoordinateCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPosts))]
    public partial int PostCount { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnassignedImages))]
    public partial int UnassignedImageCount { get; set; }

    public bool HasUnassignedImages => UnassignedImageCount > 0;
    public bool HasScenes => SceneCount > 0;
    public bool HasCharacters => CharacterCount > 0;
    public bool HasCoordinates => CoordinateCount > 0;
    public bool HasPosts => PostCount > 0;

    [ObservableProperty]
    public partial bool IsLoadingPosts { get; set; }

    public bool CanLoadPosts => Author is { } author
                                && _authorPostService?.CanScanPosts(author.Key) == true;

    public void Load(AuthorSummary summary)
    {
        Author = summary.Display;
        var key = summary.Display.Key;

        // The author-detail page is cached so returning from a child page is
        // instant. When that cached instance is later reused for a different
        // author, never leave the previous author's post content visible while
        // the new scan is running.
        Posts.Clear();
        PostGroups.Clear();
        PostPreviews.Clear();
        PostCount = 0;
        UnassignedImages.Clear();
        UnassignedImageCount = 0;

        Scenes.Clear();
        foreach (var card in _galleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Scenes.Add(card);
        }
        SceneCount = Scenes.Count;

        Characters.Clear();
        foreach (var card in _characterGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Characters.Add(card);
        }
        CharacterCount = Characters.Count;

        Coordinates.Clear();
        foreach (var card in _coordinateGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Coordinates.Add(card);
        }
        CoordinateCount = Coordinates.Count;

        RefreshOverviewPreviews();

        OnPropertyChanged(nameof(CanLoadPosts));
    }

    public async Task LoadPostsAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Author is null || !CanLoadPosts) return;

        IsLoadingPosts = true;
        try
        {
            var scanResult = await postService.ScanAuthorPostDataAsync(Author.Key, ct);
            var posts = scanResult.Posts;
            var imageAspectRatios = BuildImageAspectRatios();
            var groups = await Task.Run(
                () => posts.Select(p => new PostImageGroupViewModel(p, imageAspectRatios)).ToList(), ct);

            Posts.Clear();
            PostGroups.Clear();
            PostPreviews.Clear();
            for (int i = 0; i < posts.Count; i++)
            {
                Posts.Add(posts[i]);
                PostGroups.Add(groups[i]);
            }
            PostCount = Posts.Count;
            RefreshOverviewPreviews();

            UnassignedImages.Clear();
            foreach (var image in scanResult.UnassignedImages)
                UnassignedImages.Add(image);
            UnassignedImageCount = UnassignedImages.Count;
        }
        catch (OperationCanceledException ex) { _logger.LogError("AuthorDetail.LoadPostsCanceled", ex, Author?.Key.Id); }
        finally
        {
            IsLoadingPosts = false;
        }
    }

    public async Task AssignUnassignedImagesAsync(
        AuthorPostService postService,
        IReadOnlyList<UnassignedAuthorImage> images,
        string artworkId,
        CancellationToken ct)
    {
        if (Author is null || images.Count == 0)
            return;

        await postService.AssignUnclassifiedImagesAsync(
            Author.Key,
            images,
            artworkId,
            ct);
        await LoadPostsAsync(postService, ct);
    }

    public void SetOverviewPreviewWidth(double width)
    {
        if (width <= 0 || Math.Abs(width - _overviewPreviewWidth) < 1)
            return;

        _overviewPreviewWidth = width;
        RefreshOverviewPreviews();
    }

    private void RefreshOverviewPreviews()
    {
        FillPreviews(ScenePreviews, Scenes, card => GetPreviewWidth(card.Width, card.Height));
        FillPreviews(CharacterPreviews, Characters, card => GetPreviewWidth(card.Width, card.Height));
        FillPreviews(CoordinatePreviews, Coordinates, card => GetPreviewWidth(card.Width, card.Height));
        FillPreviews(PostPreviews, PostGroups, group => GetPreviewWidth(group.CoverAspectRatio));
    }

    private void FillPreviews<T>(
        ObservableCollection<T> target,
        IEnumerable<T> source,
        Func<T, double> getWidth)
    {
        target.Clear();
        double usedWidth = 0;
        foreach (var item in source)
        {
            var itemWidth = getWidth(item) + PreviewSpacing;
            if (target.Count > 0 && usedWidth + itemWidth > _overviewPreviewWidth)
                break;

            target.Add(item);
            usedWidth += itemWidth;
        }
    }

    private static double GetPreviewWidth(int width, int height) =>
        width > 0 && height > 0
            ? Math.Clamp(PreviewHeight * width / height, 72, 320)
            : PreviewHeight * 4.0 / 3.0;

    private static double GetPreviewWidth(double aspectRatio) =>
        Math.Clamp(PreviewHeight * aspectRatio, 72, 320);

    private Dictionary<string, double> BuildImageAspectRatios()
    {
        var ratios = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        AddRatios(_galleryViewModel.Cards);
        AddRatios(_characterGalleryViewModel.Cards);
        AddRatios(_coordinateGalleryViewModel.Cards);
        return ratios;

        void AddRatios<TCard>(IEnumerable<TCard> cards) where TCard : CardBase
        {
            foreach (var card in cards)
            {
                if (card.Width > 0 && card.Height > 0)
                    ratios[card.FilePath] = (double)card.Width / card.Height;
            }
        }
    }
}

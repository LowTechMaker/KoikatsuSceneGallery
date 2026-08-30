using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public sealed class PostImageGroupViewModel
{
    public AuthorPost Post { get; }
    public ObservableCollection<LocalImagePreview> Images { get; } = [];

    public PostImageGroupViewModel(AuthorPost post) => Post = post;
}

public partial class AuthorDetailViewModel : ObservableObject
{
    private readonly AuthorPostService? _authorPostService;
    private readonly GalleryViewModel _galleryViewModel;
    private readonly CharacterGalleryViewModel _characterGalleryViewModel;
    private readonly CoordinateGalleryViewModel _coordinateGalleryViewModel;
    private readonly ThumbnailCacheService _thumbnailCacheService;
    private readonly IAppLogger _logger;
    private AuthorDisplay? _subscribedAuthor;

    public AuthorDetailViewModel(
        AuthorPostService? authorPostService,
        GalleryViewModel galleryViewModel,
        CharacterGalleryViewModel characterGalleryViewModel,
        CoordinateGalleryViewModel coordinateGalleryViewModel,
        ThumbnailCacheService thumbnailCacheService,
        IAppLogger logger)
    {
        _authorPostService = authorPostService;
        _galleryViewModel = galleryViewModel;
        _characterGalleryViewModel = characterGalleryViewModel;
        _coordinateGalleryViewModel = coordinateGalleryViewModel;
        _thumbnailCacheService = thumbnailCacheService;
        _logger = logger;
    }

    [ObservableProperty]
    public partial AuthorDisplay? Author { get; set; }

    public string AuthorName => Author?.Name ?? "";

    public BitmapImage? AuthorAvatarSource => Author?.AvatarSource;

    public ObservableCollection<SceneCard> Scenes { get; } = [];
    public ObservableCollection<CharacterCard> Characters { get; } = [];
    public ObservableCollection<CoordinateCard> Coordinates { get; } = [];
    public ObservableCollection<AuthorPost> Posts { get; } = [];
    public ObservableCollection<PostImageGroupViewModel> PostGroups { get; } = [];

    [ObservableProperty]
    public partial int SceneCount { get; set; }

    [ObservableProperty]
    public partial int CharacterCount { get; set; }

    [ObservableProperty]
    public partial int CoordinateCount { get; set; }

    [ObservableProperty]
    public partial int PostCount { get; set; }

    [ObservableProperty]
    public partial bool IsLoadingPosts { get; set; }

    public bool CanLoadPosts => Author is { } author
                                && _authorPostService?.CanScanPosts(author.Key) == true;

    partial void OnAuthorChanging(AuthorDisplay? value)
    {
        UnsubscribeAuthorChanges();
    }

    partial void OnAuthorChanged(AuthorDisplay? value)
    {
        SubscribeAuthorChanges(value);

        OnPropertyChanged(nameof(AuthorName));
        OnPropertyChanged(nameof(AuthorAvatarSource));
        OnPropertyChanged(nameof(CanLoadPosts));
    }

    private void SubscribeAuthorChanges(AuthorDisplay? author)
    {
        if (ReferenceEquals(_subscribedAuthor, author))
            return;

        UnsubscribeAuthorChanges();
        if (author is null)
            return;

        author.PropertyChanged += Author_PropertyChanged;
        _subscribedAuthor = author;
    }

    private void UnsubscribeAuthorChanges()
    {
        if (_subscribedAuthor is null)
            return;

        _subscribedAuthor.PropertyChanged -= Author_PropertyChanged;
        _subscribedAuthor = null;
    }

    private void Author_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthorDisplay.Name))
            OnPropertyChanged(nameof(AuthorName));
        else if (e.PropertyName == nameof(AuthorDisplay.AvatarSource))
            OnPropertyChanged(nameof(AuthorAvatarSource));
    }

    public void Load(AuthorSummary summary)
    {
        Author = summary.Display;
        // Load can be called with the same Author instance after a cached page
        // was unloaded, in which case the generated property setter is a no-op.
        SubscribeAuthorChanges(Author);
        var key = summary.Display.Key;

        Posts.Clear();
        PostGroups.Clear();
        PostCount = 0;
        IsLoadingPosts = false;

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

        OnPropertyChanged(nameof(CanLoadPosts));
    }

    public void Unload()
    {
        UnsubscribeAuthorChanges();
        Author = null;
        Scenes.Clear();
        Characters.Clear();
        Coordinates.Clear();
        Posts.Clear();
        PostGroups.Clear();
        SceneCount = 0;
        CharacterCount = 0;
        CoordinateCount = 0;
        PostCount = 0;
        IsLoadingPosts = false;
    }

    public async Task LoadPostsAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Author is null || !CanLoadPosts) return;

        IsLoadingPosts = true;
        try
        {
            var posts = await postService.ScanAuthorPostsAsync(Author.Key, ct);
            ct.ThrowIfCancellationRequested();
            Posts.Clear();
            PostGroups.Clear();
            foreach (var post in posts)
            {
                Posts.Add(post);
                PostGroups.Add(new PostImageGroupViewModel(post));
            }
            PostCount = Posts.Count;

            foreach (var group in PostGroups)
            {
                foreach (var path in group.Post.LocalFilePaths.Where(File.Exists))
                {
                    ct.ThrowIfCancellationRequested();
                    var fileInfo = new FileInfo(path);
                    var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(
                        path,
                        fileInfo.Length,
                        fileInfo.LastWriteTime,
                        ct);
                    ct.ThrowIfCancellationRequested();
                    group.Images.Add(new LocalImagePreview(
                        thumbnailPath is null
                            ? null
                            : new BitmapImage(new Uri(thumbnailPath)) { DecodePixelWidth = 160 },
                        Path.GetFileName(path),
                        path));
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            IsLoadingPosts = false;
        }
    }
}

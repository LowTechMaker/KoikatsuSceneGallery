using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Media.Imaging;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public sealed record LocalImagePreview(BitmapImage? ThumbnailSource, string FileName, string FilePath);

public partial class PostDetailViewModel : ObservableObject
{
    private readonly IAppLogger _logger;
    private readonly ThumbnailCacheService _thumbnailCacheService;

    public PostDetailViewModel(
        IAppLogger logger,
        ThumbnailCacheService thumbnailCacheService)
    {
        _logger = logger;
        _thumbnailCacheService = thumbnailCacheService;
    }

    public AuthorPost? Post { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTags))]
    public partial ObservableCollection<TagDisplay> Tags { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLocalImages))]
    public partial ObservableCollection<LocalImagePreview> LocalImages { get; set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDescription))]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial string DisplayTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ArtworkIdText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedStatusText))]
    public partial bool IsDetailLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavedStatusText))]
    public partial bool IsSaved { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowRating))]
    public partial string RatingText { get; set; } = "";

    [ObservableProperty]
    public partial string LocalFileInfo { get; set; } = "";

    public bool HasTags => Tags.Count > 0;
    public bool HasLocalImages => LocalImages.Count > 0;
    public bool HasDescription => !string.IsNullOrEmpty(Description);
    public bool ShowRating => !string.IsNullOrEmpty(RatingText);

    public string SavedStatusText => IsSaved
        ? "Details saved locally"
        : IsDetailLoaded ? "Not saved" : "";

    public void Load(AuthorPost post)
    {
        Post = post;
        DisplayTitle = post.DisplayTitle;
        ArtworkIdText = $"{post.ArtworkId.ProviderId} #{post.ArtworkId.Id}";
        LocalFileInfo = post.LocalFileCount == 1
            ? "1 local file"
            : $"{post.LocalFileCount} local files";
        LocalImages.Clear();
        IsDetailLoaded = post.IsDetailLoaded;
        IsSaved = post.IsSaved;

        if (post.IsDetailLoaded)
            ApplyDetail(post);
    }

    public async Task LoadLocalImagesAsync(AuthorPost post, CancellationToken ct)
    {
        foreach (var path in post.LocalFilePaths.Where(File.Exists))
        {
            ct.ThrowIfCancellationRequested();
            var thumbnailPath = await _thumbnailCacheService.EnsureThumbnailAsync(
                path,
                File.GetLastWriteTime(path),
                ct);
            LocalImages.Add(new LocalImagePreview(
                thumbnailPath is null
                    ? null
                    : new BitmapImage(new Uri(thumbnailPath)) { DecodePixelWidth = 240 },
                Path.GetFileName(path),
                path));
        }

        OnPropertyChanged(nameof(HasLocalImages));
    }

    public async Task LoadDetailAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Post is null) return;

        IsLoading = true;
        try
        {
            var info = await postService.FetchArtworkDetailAsync(Post, ct);
            if (info is not null)
            {
                ApplyInfo(info);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError("PostDetail.Load", ex, Post?.ArtworkId.Id);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyInfo(ArtworkInfo info)
    {
        if (Post is null) return;

        Post.Title = info.Title;
        Post.Description = info.Description;
        Post.Rating = info.Rating;
        Post.Tags = info.Tags;
        Post.IsDetailLoaded = true;
        Post.IsSaved = info.IsSavedLocally;

        ApplyDetail(Post);
        DisplayTitle = Post.DisplayTitle;
        IsDetailLoaded = true;
        IsSaved = info.IsSavedLocally;
    }

    private void ApplyDetail(AuthorPost post)
    {
        Description = post.Description;

        RatingText = post.Rating switch
        {
            ContentRating.R18 => "R-18",
            ContentRating.R18G => "R-18G",
            _ => "",
        };

        Tags.Clear();
        foreach (var tag in post.Tags ?? Array.Empty<ArtworkTag>())
        {
            var display = !string.IsNullOrWhiteSpace(tag.TranslatedName)
                ? $"{tag.Name} ({tag.TranslatedName})"
                : tag.Name;
            Tags.Add(new TagDisplay(display));
        }
        OnPropertyChanged(nameof(HasTags));
    }
}

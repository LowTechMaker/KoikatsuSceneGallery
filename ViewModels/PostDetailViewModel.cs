using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Pages;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public sealed record LocalImagePreview(Uri ImageUri, string FileName, string FilePath);

public partial class PostDetailViewModel : ObservableObject
{
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
    [NotifyPropertyChangedFor(nameof(CanSave))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(SavedStatusText))]
    public partial bool IsDetailLoaded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
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
    public bool CanSave => IsDetailLoaded && !IsSaved && !IsLoading;

    public string SavedStatusText => IsSaved
        ? "Details saved locally"
        : IsDetailLoaded ? "Not saved" : "";

    public void Load(AuthorPost post)
    {
        Post = post;
        DisplayTitle = post.DisplayTitle;
        ArtworkIdText = $"pixiv #{post.ArtworkId.Id}";
        LocalFileInfo = post.LocalFileCount == 1
            ? "1 local file"
            : $"{post.LocalFileCount} local files";
        LocalImages = new ObservableCollection<LocalImagePreview>(
            post.LocalFilePaths
                .Where(File.Exists)
                .Select(path => new LocalImagePreview(new Uri(path), Path.GetFileName(path), path)));
        IsDetailLoaded = post.IsDetailLoaded;
        IsSaved = post.IsSaved;

        if (post.IsDetailLoaded)
            ApplyDetail(post);
    }

    public async Task LoadDetailAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Post is null) return;

        IsLoading = true;
        try
        {
            var info = await postService.FetchArtworkDetailAsync(
                Post.ArtworkId,
                ct,
                saveToLocalCache: false);
            if (info is not null)
            {
                ApplyInfo(info);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Helpers.CrashLog.Write("PostDetail", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task SaveToCacheAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Post is null || !IsDetailLoaded || IsSaved) return;

        IsLoading = true;
        try
        {
            var info = await postService.FetchArtworkDetailAsync(
                Post.ArtworkId,
                ct,
                saveToLocalCache: true);
            if (info is not null)
                ApplyInfo(info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Helpers.CrashLog.Write("PostDetail.Save", ex);
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

        Tags = new ObservableCollection<TagDisplay>(
            (post.Tags ?? Array.Empty<ArtworkTag>())
            .Select(tag =>
            {
                var display = !string.IsNullOrWhiteSpace(tag.TranslatedName)
                    ? $"{tag.Name} ({tag.TranslatedName})"
                    : tag.Name;
                return new TagDisplay(display);
            }));
    }
}

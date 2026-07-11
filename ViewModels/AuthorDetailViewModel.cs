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

    public PostImageGroupViewModel(AuthorPost post)
    {
        Post = post;
        foreach (var path in post.LocalFilePaths.Where(File.Exists))
            Images.Add(new LocalImagePreview(new Uri(path.Replace("#", "%23")), Path.GetFileName(path), path));
    }
}

public partial class AuthorDetailViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AuthorDisplay? Author { get; set; }

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
                                && App.AuthorPostService?.CanScanPosts(author.Key) == true;

    public void Load(AuthorSummary summary)
    {
        Author = summary.Display;
        var key = summary.Display.Key;

        Scenes.Clear();
        foreach (var card in App.GalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Scenes.Add(card);
        }
        SceneCount = Scenes.Count;

        Characters.Clear();
        foreach (var card in App.CharacterGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Characters.Add(card);
        }
        CharacterCount = Characters.Count;

        Coordinates.Clear();
        foreach (var card in App.CoordinateGalleryViewModel.Cards)
        {
            if (card.Author?.Key == key)
                Coordinates.Add(card);
        }
        CoordinateCount = Coordinates.Count;

        OnPropertyChanged(nameof(CanLoadPosts));
    }

    public async Task LoadPostsAsync(AuthorPostService postService, CancellationToken ct)
    {
        if (Author is null || !CanLoadPosts) return;

        IsLoadingPosts = true;
        try
        {
            var posts = await postService.ScanAuthorPostsAsync(Author.Key, ct);
            var groups = await Task.Run(
                () => posts.Select(p => new PostImageGroupViewModel(p)).ToList(), ct);

            Posts.Clear();
            PostGroups.Clear();
            for (int i = 0; i < posts.Count; i++)
            {
                Posts.Add(posts[i]);
                PostGroups.Add(groups[i]);
            }
            PostCount = Posts.Count;
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsLoadingPosts = false;
        }
    }
}

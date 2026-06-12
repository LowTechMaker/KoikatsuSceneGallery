using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace KoikatsuSceneGallery.Pages;

public sealed record TagDisplay(string Display);

public sealed partial class PostDetailPage : Page
{
    public PostDetailViewModel ViewModel { get; } = new();

    private CancellationTokenSource? _cts;

    public PostDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AuthorPost post)
        {
            ViewModel.Load(post);
            if (!post.IsDetailLoaded && App.AuthorPostService is { } postService)
            {
                _cts = new CancellationTokenSource();
                _ = ViewModel.LoadDetailAsync(postService, _cts.Token);
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

    private async void OpenInBrowser_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Post is { } post)
            await Windows.System.Launcher.LaunchUriAsync(new Uri(post.ArtworkUrl));
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (App.AuthorPostService is { } postService)
            await ViewModel.SaveToCacheAsync(postService, _cts?.Token ?? CancellationToken.None);
    }
}

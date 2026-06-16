using System.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace KoikatsuSceneGallery.Pages;

public sealed record TagDisplay(string Display);

public sealed partial class PostDetailPage : Page
{
    public PostDetailViewModel ViewModel { get; } = new();

    private CancellationTokenSource? _cts;

    public PostDetailPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is AuthorPost post)
        {
            ViewModel.Load(post);
            RenderDescription();
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

    private void GoBack_Click(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); }

    private void LocalImage_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not LocalImagePreview preview) return;

        var path = preview.FilePath;
        var scene = App.GalleryViewModel.Cards.FirstOrDefault(c => c.FilePath == path);
        if (scene is not null) { Frame.Navigate(typeof(DetailPage), scene); return; }

        var character = App.CharacterGalleryViewModel.Cards.FirstOrDefault(c => c.FilePath == path);
        if (character is not null) { Frame.Navigate(typeof(CharacterDetailPage), character); return; }

        var coordinate = App.CoordinateGalleryViewModel.Cards.FirstOrDefault(c => c.FilePath == path);
        if (coordinate is not null) { Frame.Navigate(typeof(CoordinateDetailPage), coordinate); return; }
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

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PostDetailViewModel.Description))
            RenderDescription();
    }

    private async void LocalImagesGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        var files = new List<StorageFile>();
        foreach (var item in e.Items)
        {
            if (item is LocalImagePreview preview)
            {
                try { files.Add(await StorageFile.GetFileFromPathAsync(preview.FilePath)); }
                catch { }
            }
        }
        if (files.Count > 0)
        {
            e.Data.SetStorageItems(files);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        }
    }

    private void RenderDescription()
        => HtmlDescriptionRenderer.Render(DescriptionText, ViewModel.Description);
}

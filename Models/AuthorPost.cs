using CommunityToolkit.Mvvm.ComponentModel;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Models;

public partial class AuthorPost : ObservableObject
{
    public required ArtworkId ArtworkId { get; init; }
    public required string ArtworkUrl { get; init; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial string? Description { get; set; }

    [ObservableProperty]
    public partial ContentRating Rating { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ArtworkTag>? Tags { get; set; }

    [ObservableProperty]
    public partial bool IsDetailLoaded { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    public partial bool IsSaved { get; set; }

    public int LocalFileCount { get; init; }

    public IReadOnlyList<string> LocalFilePaths { get; init; } = [];

    public string DisplayTitle => Title ?? ArtworkId.Id;

    partial void OnTitleChanged(string? value) => OnPropertyChanged(nameof(DisplayTitle));
}

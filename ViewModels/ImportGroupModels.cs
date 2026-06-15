using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

public sealed record SelectableAuthor(string Name, string Id)
{
    public string DisplayText => $"{Name} ({Id})";
}

public partial class ImportRatingGroup : ObservableObject
{
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public ContentRating Rating { get; }
    public string Label { get; }
    public ObservableCollection<ImportAuthorGroup> Authors { get; } = [];

    public ImportRatingGroup(ContentRating rating)
    {
        Rating = rating;
        Label = rating switch
        {
            ContentRating.R18G => "R-18G",
            ContentRating.R18  => "R-18",
            _                  => "G",
        };
    }
}

public partial class ImportAuthorGroup : ObservableObject
{
    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string AuthorName { get; }
    public string AuthorId { get; }
    public ObservableCollection<ImportArtworkGroup> Artworks { get; } = [];

    public string HeaderText => $"{AuthorName} ({AuthorId})";

    public ImportAuthorGroup(string authorName, string authorId)
    {
        AuthorName = authorName;
        AuthorId = authorId;
    }
}

public partial class ImportArtworkGroup : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsSauceNaoSearching { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string? Title { get; }
    public string ArtworkId { get; }
    public ObservableCollection<ImportItem> Files { get; } = [];

    public string HeaderText => string.IsNullOrEmpty(Title)
        ? $"({ArtworkId})"
        : $"{Title} ({ArtworkId})";

    public int FileCount => Files.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignAuthor))]
    public partial string ManualAuthorId { get; set; } = "";

    public bool CanAssignAuthor => !string.IsNullOrWhiteSpace(ManualAuthorId);

    public ImportArtworkGroup(string? title, string artworkId)
    {
        Title = title;
        ArtworkId = artworkId;
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(HeaderText));
        };
    }
}

public partial class ImportUnknownGroup : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsSauceNaoSearching { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string GroupId { get; }
    public ObservableCollection<ImportItem> Files { get; } = [];

    public string HeaderText => Files.Count == 1
        ? Files[0].FileName
        : $"{GroupId} ({FileCount})";

    public int FileCount => Files.Count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignAuthor))]
    public partial string ManualAuthorId { get; set; } = "";

    public bool CanAssignAuthor => !string.IsNullOrWhiteSpace(ManualAuthorId);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignArtworkId))]
    public partial string ManualArtworkId { get; set; } = "";

    public bool CanAssignArtworkId => !string.IsNullOrWhiteSpace(ManualArtworkId);

    public ImportUnknownGroup(string groupId, IEnumerable<ImportItem> items)
    {
        GroupId = groupId;
        foreach (var item in items)
            Files.Add(item);
        Files.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FileCount));
            OnPropertyChanged(nameof(HeaderText));
        };
    }
}

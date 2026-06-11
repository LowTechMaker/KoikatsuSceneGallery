using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Models;

public enum ImportItemStatus
{
    Pending,
    Analyzing,
    ReadyToImport,
    Importing,
    Completed,
    Failed,
    Skipped,
}

public partial class ImportItem : ObservableObject
{
    public required string SourceFilePath { get; init; }
    public string FileName => Path.GetFileName(SourceFilePath);

    [ObservableProperty]
    public partial ArtworkId? ArtworkId { get; set; }

    [ObservableProperty]
    public partial CardType CardType { get; set; }

    [ObservableProperty]
    public partial ContentRating Rating { get; set; }

    [ObservableProperty]
    public partial string? AuthorName { get; set; }

    [ObservableProperty]
    public partial string? AuthorId { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ArtworkTag>? Tags { get; set; }

    [ObservableProperty]
    public partial ImportItemStatus Status { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? DestinationPath { get; set; }

    [ObservableProperty]
    public partial bool IsExcluded { get; set; }

    // Display helpers for XAML binding
    public string CardTypeText => CardType switch
    {
        CardType.Scene => "Scene",
        CardType.Character => "Chara",
        CardType.Coordinate => "Coord",
        _ => "—",
    };

    public string RatingText => Rating switch
    {
        ContentRating.R18 => "R-18",
        ContentRating.R18G => "R-18G",
        _ => "",
    };

    public bool IsR18OrAbove => Rating is ContentRating.R18 or ContentRating.R18G;

    public string StatusGlyph => Status switch
    {
        ImportItemStatus.Completed => "",  // Checkmark
        ImportItemStatus.Failed => "",      // Error
        ImportItemStatus.Skipped => "",     // Cancel
        _ => "",
    };

    public bool ShowStatusGlyph => Status is ImportItemStatus.Completed or ImportItemStatus.Failed or ImportItemStatus.Skipped;
    public bool ShowStatusRing => Status is ImportItemStatus.Analyzing or ImportItemStatus.Importing;
    public bool HasAuthor => AuthorName is not null;
    public bool HasDestination => DestinationPath is not null;

    partial void OnCardTypeChanged(CardType value) => OnPropertyChanged(nameof(CardTypeText));
    partial void OnRatingChanged(ContentRating value)
    {
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(IsR18OrAbove));
    }
    partial void OnStatusChanged(ImportItemStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(ShowStatusGlyph));
        OnPropertyChanged(nameof(ShowStatusRing));
    }
    partial void OnAuthorNameChanged(string? value) => OnPropertyChanged(nameof(HasAuthor));
    partial void OnDestinationPathChanged(string? value) => OnPropertyChanged(nameof(HasDestination));
}

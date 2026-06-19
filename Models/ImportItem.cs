using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Models;

public enum ImportItemStatus
{
    Pending,
    Analyzing,
    ReadyToImport,
    AlreadyInLibrary,
    Importing,
    Completed,
    Failed,
    Skipped,
}

public partial class ImportItem : ObservableObject
{
    public required string SourceFilePath { get; init; }
    public string FileName => Path.GetFileName(SourceFilePath);
    public Uri ThumbnailUri => new(SourceFilePath);
    public string SourceFolder => Path.GetDirectoryName(SourceFilePath)!;

    public ulong? PHash { get; set; }
    public float[]? ColorHistogram { get; set; }
    public IReadOnlyList<ContentRating> RatingOptions { get; } =
        [ContentRating.AllAges, ContentRating.R18, ContentRating.R18G];

    [ObservableProperty]
    public partial ArtworkId? ArtworkId { get; set; }

    [ObservableProperty]
    public partial CardType CardType { get; set; }

    [ObservableProperty]
    public partial GameVersion GameVersion { get; set; }

    [ObservableProperty]
    public partial ContentRating Rating { get; set; }

    [ObservableProperty]
    public partial string? AuthorName { get; set; }

    [ObservableProperty]
    public partial string? AuthorId { get; set; }

    [ObservableProperty]
    public partial string? AuthorProviderId { get; set; }

    [ObservableProperty]
    public partial string? Title { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<ArtworkTag>? Tags { get; set; }

    [ObservableProperty]
    public partial ImportItemStatus Status { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string? DestinationPath { get; set; }

    // Display helpers for XAML binding
    public string CardTypeText
    {
        get
        {
            var type = CardType switch
            {
                CardType.Scene      => "Scene",
                CardType.Character  => "Chara",
                CardType.Coordinate => "Coord",
                _ => null,
            };
            if (type is null) return "—";
            return GameVersion switch
            {
                GameVersion.Koikatsu         => $"KK {type}",
                GameVersion.KoikatsuSunshine => $"KKS {type}",
                _ => type,
            };
        }
    }

    public string RatingText => Rating switch
    {
        ContentRating.R18  => "R-18",
        ContentRating.R18G => "R-18G",
        _ => "",
    };

    public bool IsR18OrAbove => Rating is ContentRating.R18 or ContentRating.R18G;

    public int RatingIndex
    {
        get => Rating switch
        {
            ContentRating.R18  => 1,
            ContentRating.R18G => 2,
            _                  => 0,
        };
        set => Rating = value switch
        {
            1 => ContentRating.R18,
            2 => ContentRating.R18G,
            _ => ContentRating.AllAges,
        };
    }

    public bool? IsAllAgesRating
    {
        get => Rating == ContentRating.AllAges;
        set
        {
            if (value == true)
                Rating = ContentRating.AllAges;
        }
    }

    public bool? IsR18Rating
    {
        get => Rating == ContentRating.R18;
        set
        {
            if (value == true)
                Rating = ContentRating.R18;
        }
    }

    public bool? IsR18GRating
    {
        get => Rating == ContentRating.R18G;
        set
        {
            if (value == true)
                Rating = ContentRating.R18G;
        }
    }

    public bool IsAlreadyInLibrary => Status == ImportItemStatus.AlreadyInLibrary;

    public string StatusGlyph => Status switch
    {
        ImportItemStatus.Completed        => "",  // Accept checkmark
        ImportItemStatus.Failed           => "",  // Error badge
        ImportItemStatus.Skipped          => "",  // Cancel
        ImportItemStatus.AlreadyInLibrary => "",  // Library
        _ => "",
    };

    public bool ShowStatusGlyph => Status is ImportItemStatus.Completed or ImportItemStatus.Failed
        or ImportItemStatus.Skipped or ImportItemStatus.AlreadyInLibrary;
    public bool ShowStatusRing => Status is ImportItemStatus.Analyzing or ImportItemStatus.Importing;
    public bool HasAuthor => AuthorName is not null;
    public bool HasDestination => DestinationPath is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignAuthor))]
    public partial string? ManualAuthorId { get; set; }

    public bool CanAssignAuthor => !string.IsNullOrWhiteSpace(ManualAuthorId);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAssignArtworkId))]
    public partial string? ManualArtworkId { get; set; }

    public bool CanAssignArtworkId => !string.IsNullOrWhiteSpace(ManualArtworkId);

    partial void OnCardTypeChanged(CardType value) => OnPropertyChanged(nameof(CardTypeText));
    partial void OnGameVersionChanged(GameVersion value) => OnPropertyChanged(nameof(CardTypeText));
    partial void OnRatingChanged(ContentRating value)
    {
        OnPropertyChanged(nameof(RatingText));
        OnPropertyChanged(nameof(IsR18OrAbove));
        OnPropertyChanged(nameof(RatingIndex));
        OnPropertyChanged(nameof(IsAllAgesRating));
        OnPropertyChanged(nameof(IsR18Rating));
        OnPropertyChanged(nameof(IsR18GRating));
    }
    partial void OnStatusChanged(ImportItemStatus value)
    {
        OnPropertyChanged(nameof(StatusGlyph));
        OnPropertyChanged(nameof(ShowStatusGlyph));
        OnPropertyChanged(nameof(ShowStatusRing));
        OnPropertyChanged(nameof(IsAlreadyInLibrary));
    }
    partial void OnAuthorNameChanged(string? value) => OnPropertyChanged(nameof(HasAuthor));
    partial void OnDestinationPathChanged(string? value) => OnPropertyChanged(nameof(HasDestination));
}

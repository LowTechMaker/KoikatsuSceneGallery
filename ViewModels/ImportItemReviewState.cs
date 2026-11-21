using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>
/// UI-only state for a flattened import-review row. Selection deliberately
/// stays outside <see cref="ImportItem"/> so the legacy group workflows keep
/// their own selection semantics.
/// </summary>
public sealed partial class ImportItemReviewState : ObservableObject, IDisposable
{
    private readonly string _unassignedText;
    private readonly Func<string, string> _text;

    public ImportItem Item { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial double TileWidth { get; set; } = 208;

    public bool CanImport => Services.ImportReviewPolicy.CanExecute(Item.Status, Item.DestinationPath);
    public bool IsIdentified => Services.ImportReviewPolicy.IsIdentified(Item.Status, !string.IsNullOrWhiteSpace(Item.AuthorId));
    public Helpers.ImportReviewSection Section => Item.UserSkipped ? Helpers.ImportReviewSection.Skipped : Services.ImportReviewPolicy.Section(Item.Status,
        !string.IsNullOrWhiteSpace(Item.AuthorId), Item.ArtworkId is not null);
    public bool CanSelect => !Item.IsAlreadyInLibrary;
    public string DisplayRating => Item.Rating == SceneGallery.PluginSdk.ContentRating.AllAges ? "G" : Item.RatingText;
    public string GroupKey => Item.ArtworkId is { } artwork
        ? $"post:{artwork.ProviderId}:{artwork.Id}"
        : "folder:" + Item.SourceFolder;
    public string GroupTitle => Item.ArtworkId is { } artwork
        ? $"{artwork.ProviderId} · {artwork.Id}" + (string.IsNullOrWhiteSpace(Item.Title) ? "" : $" · {Item.Title}")
        : System.IO.Path.GetFileName(Item.SourceFolder.TrimEnd(System.IO.Path.DirectorySeparatorChar)) is { Length: > 0 } folder
            ? folder : Item.SourceFolder;
    public string Category => Services.ImportReviewPolicy.Category(Item.Status, Item.DestinationPath,
        !string.IsNullOrWhiteSpace(Item.AuthorId), Item.ArtworkId is not null);
    public string StatusText => Item.UserSkipped ? _text("Import_Status_Skipped")
        : Item.IsInLibraryElsewhere ? _text("Import_Status_ExistingElsewhere")
        : _text("Import_Status_" + Category);

    /// <summary>The colour family of the status chip.</summary>
    public string Tone => Item.UserSkipped ? "Muted" : Category switch
    {
        "NeedsInfo" or "Failed" => "Attention",
        "Ready" => "Ready",
        "Analyzing" => "Busy",
        _ => "Muted",
    };

    /// <summary>Author, post and card type: what is known about the card, without repeating the status.</summary>
    public string MetaText => IsIdentified && !string.IsNullOrWhiteSpace(Item.AuthorName)
        ? string.Join("  ·  ", new[] { Item.AuthorName, DisplayArtworkKeyOrNull, Item.CardTypeText }.Where(part => !string.IsNullOrEmpty(part)))
        : Item.CardTypeText;

    private string? DisplayArtworkKeyOrNull => Item.ArtworkId is null || string.IsNullOrWhiteSpace(Item.ArtworkId.Id)
        ? null : $"{Item.ArtworkId.ProviderId}:{Item.ArtworkId.Id}";

    /// <summary>A third line when there is something worth saying: an error, where the library has it, or the archive it came from.</summary>
    public string NoteText => Item.ErrorMessage
        ?? (Item.IsInLibraryElsewhere ? string.Format(_text("Import_Review_InLibraryAt"), Item.ExistingLibraryPath) : null)
        ?? (Item.ArchivePath is { } archive ? string.Format(_text("Import_Review_FromArchive"), System.IO.Path.GetFileName(archive)) : null)
        ?? string.Empty;

    public bool HasNote => NoteText.Length > 0;
    public string DestinationText => Item.ErrorMessage ?? (!IsIdentified ? StatusText : Item.DestinationPath) ?? _text("Import_Status_NeedsInfo");

    public string DisplayAuthorName => string.IsNullOrWhiteSpace(Item.AuthorName)
        ? _unassignedText
        : Item.AuthorName;

    public string DisplayArtworkKey => Item.ArtworkId is null || string.IsNullOrWhiteSpace(Item.ArtworkId.Id)
        ? _unassignedText
        : $"{Item.ArtworkId.ProviderId}:{Item.ArtworkId.Id}";

    public ImportItemReviewState(ImportItem item, string unassignedText, Func<string, string> text)
    {
        Item = item;
        _unassignedText = unassignedText;
        _text = text;
        Item.PropertyChanged += OnItemPropertyChanged;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(IsIdentified));
        OnPropertyChanged(nameof(Section));
        OnPropertyChanged(nameof(CanSelect));
        OnPropertyChanged(nameof(DisplayRating));
        OnPropertyChanged(nameof(Category));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DestinationText));
        OnPropertyChanged(nameof(Tone));
        OnPropertyChanged(nameof(MetaText));
        OnPropertyChanged(nameof(NoteText));
        OnPropertyChanged(nameof(HasNote));
        switch (e.PropertyName)
        {
            case nameof(ImportItem.AuthorName):
                OnPropertyChanged(nameof(DisplayAuthorName));
                break;
            case nameof(ImportItem.ArtworkId):
                OnPropertyChanged(nameof(DisplayArtworkKey));
                break;
        }
    }

    public void Dispose() => Item.PropertyChanged -= OnItemPropertyChanged;
}

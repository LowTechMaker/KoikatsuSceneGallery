using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Models;

public sealed class GalleryEntry : ObservableObject, IDisposable
{
    private AuthorDisplay? _observedAuthor;
    public GalleryEntry(string key, IReadOnlyList<CardBase> members, bool showTitle)
    {
        Key = key;
        Members = members;
        ShowTitle = showTitle;
        Card.PropertyChanged += CardChanged;
        ObserveAuthor();
    }
    public string Key { get; }
    public IReadOnlyList<CardBase> Members { get; }
    public CardBase Card => Members[0];
    public bool IsGroup => Members.Count > 1;
    /// <summary>A third card peeks out of the stack only when there is a third card.</summary>
    public bool HasThirdLayer => Members.Count > 2;
    public bool ShowTitle { get; }
    /// <summary>
    /// The caption under the picture: title and author, or nothing at all when
    /// file names are switched off. That switch used to leave the author line
    /// behind; the user asked for the picture and the rating badge only.
    /// </summary>
    public double MetadataHeight => ShowTitle ? 52 : 0;

    private bool _isSelected;
    /// <summary>
    /// Whether the tile is selected in the grid. Kept here so the tile can draw
    /// its own strong selection mark; the grid's default one, a thin accent
    /// line, was hard to see on a picture.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    public string CountText => UiText.Format("SceneGallery_GroupCount", Members.Count);
    public AuthorDisplay? Author => (Card as IAuthorOwner)?.Author;
    public string AuthorText => Author?.Name ?? UiText.Get("SceneGallery_UnknownAuthor");
    public bool IsR18 => Card is SceneCard { IsR18Content: true };
    public string RatingText => Card is SceneCard scene
        ? scene.Rating switch { SceneRating.R18G => "R-18G", SceneRating.R18 => "R-18", _ => "G" }
        : string.Empty;
    public bool HasVersions => Card is CharacterCard { HasVersions: true };
    public string VersionText => Card is CharacterCard c ? UiText.Format("Browser_Versions", c.VersionCount) : "";
    // The tile only ever shows the character's representative, so the marker
    // says "this character has what-if versions" rather than "this card is one".
    public bool HasAlternateVersions => Card is CharacterCard { HasAlternateVersions: true };
    public string AlternateText => HasAlternateVersions ? UiText.Get("Browser_AlternateVersion") : "";
    public bool HasVersionBadge => HasVersions || HasAlternateVersions;
    public string Title
    {
        get
        {
            if (!IsGroup) return Card switch
            {
                CharacterCard c when !string.IsNullOrWhiteSpace(c.CharacterName) => c.CharacterName,
                CoordinateCard c when !string.IsNullOrWhiteSpace(c.CoordinateName) => c.CoordinateName,
                _ => Card.FileName
            };
            var folder = Path.GetFileName(Path.GetDirectoryName(Card.FilePath)) ?? Card.FileName;
            if (!Key.StartsWith("post:", StringComparison.Ordinal)) return folder;
            var identity = Key.Split(':', 3);
            return folder.Contains(identity[2], StringComparison.Ordinal) ? folder : $"{identity[1]} · {identity[2]}";
        }
    }
    private void CardChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "ThumbnailPath" or "ThumbnailUri" or "HasThumbnail") return;
        ObserveAuthor();
        OnPropertyChanged(string.Empty);
    }
    public override string ToString() => IsGroup ? $"{Title} · {CountText}" : Title;
    private void ObserveAuthor()
    {
        if (ReferenceEquals(_observedAuthor, Author)) return;
        if (_observedAuthor is not null) _observedAuthor.PropertyChanged -= AuthorChanged;
        _observedAuthor = Author;
        if (_observedAuthor is not null) _observedAuthor.PropertyChanged += AuthorChanged;
    }
    private void AuthorChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(AuthorText));
    public void Dispose()
    {
        Card.PropertyChanged -= CardChanged;
        if (_observedAuthor is not null) _observedAuthor.PropertyChanged -= AuthorChanged;
    }
}

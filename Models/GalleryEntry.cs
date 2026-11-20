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

    /// <summary>The cards fanned out behind a group's cover (the cover itself when there is no such card).</summary>
    public CardBase SecondCard => Members.Count > 1 ? Members[1] : Card;
    public CardBase ThirdCard => Members.Count > 2 ? Members[2] : SecondCard;

    /// <summary>
    /// Room around a group's cover for the cards fanned behind it, drawn like
    /// the details pane's stack of selected cards (user request); none for a
    /// single card, whose picture fills its cell.
    /// </summary>
    public Microsoft.UI.Xaml.Thickness FaceMargin => IsGroup
        ? new Microsoft.UI.Xaml.Thickness(6, 10, 14, 8)
        : new Microsoft.UI.Xaml.Thickness(0);

    /// <summary>The number on a group's count badge.</summary>
    public string MemberCountText => Members.Count.ToString();
    public bool ShowTitle { get; }
    /// <summary>
    /// The caption under the picture: title and author, or nothing at all when
    /// file names are switched off. That switch used to leave the author line
    /// behind; the user asked for the picture and the rating badge only.
    /// </summary>
    public double MetadataHeight => ShowTitle ? CaptionHeight : 0;

    /// <summary>The title and author lines under the picture (LibraryBrowser and the layout reserve).</summary>
    public const double CaptionHeight = 44;

    // Two looks, by the "show file names" setting (GalleryLayoutEngine.SetChrome):
    // photo mode, pictures edge to edge with their badges on them; card mode, a
    // framed card with everything (title, author, rating, count, versions) under
    // the picture so nothing covers it (user request).
    public const double PhotoMargin = 1, CardModeMargin = 4, CardModeInset = 5;
    public Microsoft.UI.Xaml.Thickness TileMargin => new(ShowTitle ? CardModeMargin : PhotoMargin);
    public Microsoft.UI.Xaml.Thickness PictureInset => ShowTitle
        ? new Microsoft.UI.Xaml.Thickness(CardModeInset, CardModeInset, CardModeInset, 0)
        : new Microsoft.UI.Xaml.Thickness(0);
    public bool BadgesOnPicture => !ShowTitle;
    public bool GroupBadgeOnPicture => IsGroup && !ShowTitle;
    public bool VersionBadgeOnPicture => HasVersionBadge && !ShowTitle;
    public bool VersionInCaption => HasVersionBadge && ShowTitle;
    public bool GroupInCaption => IsGroup && ShowTitle;

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

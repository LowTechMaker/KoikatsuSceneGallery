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
    public bool ShowTitle { get; }
    public double MetadataHeight => ShowTitle ? 52 : 32;
    public string CountText => UiText.Format("SceneGallery_GroupCount", Members.Count);
    public AuthorDisplay? Author => (Card as IAuthorOwner)?.Author;
    public string AuthorText => Author?.Name ?? (Card is MediaCard
        ? Card.DateModified.ToString("yyyy/MM/dd") : UiText.Get("SceneGallery_UnknownAuthor"));
    public bool IsR18 => Card is SceneCard { IsR18Content: true };
    public string RatingText => Card is SceneCard ? (IsR18 ? "R-18" : "G") : string.Empty;
    public bool HasVersions => Card is CharacterCard { HasVersions: true };
    public string VersionText => Card is CharacterCard c ? UiText.Format("Browser_Versions", c.VersionCount) : "";
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

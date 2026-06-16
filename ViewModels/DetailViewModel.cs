using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

public partial class DetailViewModel : ObservableObject
{
    private FilenameLinkInfo _linkInfo = FilenameLinkParser.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(HasPixivArtworkId))]
    [NotifyPropertyChangedFor(nameof(BepisDbId))]
    [NotifyPropertyChangedFor(nameof(BepisDbUrl))]
    [NotifyPropertyChangedFor(nameof(HasBepisDbUrl))]
    [NotifyPropertyChangedFor(nameof(Author))]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    [NotifyPropertyChangedFor(nameof(AuthorSummary))]
    public partial SceneCard? Card { get; set; }

    partial void OnCardChanged(SceneCard? value)
    {
        _linkInfo = FilenameLinkParser.Parse(value?.FilePath);
        RefreshSiblingCards();
    }

    public string? PixivArtworkId => _linkInfo.PixivArtworkId;
    public string? PixivUrl => _linkInfo.PixivUrl;
    public bool HasPixivArtworkId => _linkInfo.PixivArtworkId != null;
    public string? BepisDbId => _linkInfo.BepisDbId;
    public string? BepisDbUrl => _linkInfo.BepisDbUrl;
    public bool HasBepisDbUrl => _linkInfo.BepisDbUrl != null;

    public AuthorDisplay? Author => Card?.Author;
    public bool HasAuthor => Author != null;

    public AuthorSummary? AuthorSummary =>
        Author is { } a
            ? App.AuthorInfoService.GetSummaries().FirstOrDefault(s => s.Display == a)
            : null;

    public ObservableCollection<SceneCard> SiblingCards { get; } = [];
    public bool HasSiblingCards => SiblingCards.Count > 0;

    private void RefreshSiblingCards()
    {
        SiblingCards.Clear();
        var artworkId = _linkInfo.PixivArtworkId;
        if (artworkId is null || Card is null)
        {
            OnPropertyChanged(nameof(HasSiblingCards));
            return;
        }

        foreach (var c in App.GalleryViewModel.Cards)
        {
            if (c == Card) continue;
            var info = FilenameLinkParser.Parse(c.FilePath);
            if (info.PixivArtworkId == artworkId)
                SiblingCards.Add(c);
        }
        OnPropertyChanged(nameof(HasSiblingCards));
    }
}

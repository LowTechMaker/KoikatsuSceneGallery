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
    public bool HasSiblingCards => SiblingCards.Count > 1;

    private void RefreshSiblingCards()
    {
        if (Card is null)
        {
            SiblingCards.Clear();
            OnPropertyChanged(nameof(HasSiblingCards));
            return;
        }

        var artworkId = _linkInfo.PixivArtworkId;
        var siblings = new List<SceneCard>();

        if (artworkId is not null)
        {
            foreach (var c in App.GalleryViewModel.Cards)
            {
                var info = FilenameLinkParser.Parse(c.FilePath);
                if (info.PixivArtworkId == artworkId)
                    siblings.Add(c);
            }
        }
        else
        {
            var folder = Path.GetDirectoryName(Card.FilePath);
            if (folder is not null)
            {
                foreach (var c in App.GalleryViewModel.Cards)
                {
                    if (string.Equals(Path.GetDirectoryName(c.FilePath), folder, StringComparison.OrdinalIgnoreCase))
                        siblings.Add(c);
                }
            }
        }

        siblings.Sort((a, b) => CompareNatural(a.FileName, b.FileName));

        if (SiblingsUnchanged(siblings))
            return;

        SiblingCards.Clear();
        foreach (var c in siblings)
            SiblingCards.Add(c);

        OnPropertyChanged(nameof(HasSiblingCards));
    }

    private bool SiblingsUnchanged(List<SceneCard> newSiblings)
    {
        if (SiblingCards.Count != newSiblings.Count) return false;
        for (int i = 0; i < newSiblings.Count; i++)
        {
            if (SiblingCards[i] != newSiblings[i]) return false;
        }
        return true;
    }

    private static int CompareNatural(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;

        int i = 0;
        int j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
            {
                int numberCompare = CompareNumberRuns(left, ref i, right, ref j);
                if (numberCompare != 0) return numberCompare;
                continue;
            }

            int charCompare = char.ToUpperInvariant(left[i]).CompareTo(char.ToUpperInvariant(right[j]));
            if (charCompare != 0) return charCompare;
            i++;
            j++;
        }

        return (left.Length - i).CompareTo(right.Length - j);
    }

    private static int CompareNumberRuns(string left, ref int leftIndex, string right, ref int rightIndex)
    {
        int leftStart = leftIndex;
        int rightStart = rightIndex;
        while (leftIndex < left.Length && char.IsDigit(left[leftIndex])) leftIndex++;
        while (rightIndex < right.Length && char.IsDigit(right[rightIndex])) rightIndex++;

        ReadOnlySpan<char> leftRun = left.AsSpan(leftStart, leftIndex - leftStart).TrimStart('0');
        ReadOnlySpan<char> rightRun = right.AsSpan(rightStart, rightIndex - rightStart).TrimStart('0');

        if (leftRun.Length != rightRun.Length)
            return leftRun.Length.CompareTo(rightRun.Length);

        int digitCompare = leftRun.SequenceCompareTo(rightRun);
        if (digitCompare != 0) return digitCompare;

        return (leftIndex - leftStart).CompareTo(rightIndex - rightStart);
    }
}

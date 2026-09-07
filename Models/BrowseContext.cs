using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Pages;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Models;

public sealed class BrowseContext(string title, IEnumerable<CardBase> cards, bool showRelatedImages = false)
{
    public string Title { get; } = title;
    public bool ShowRelatedImages { get; } = showRelatedImages;
    public IReadOnlyList<CardBase> Cards { get; } = cards.DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase).ToArray();
    public string? ReturnPath { get; set; }
    public IReadOnlyList<CardBase> CurrentCards()
    {
        var live = Enum.GetValues<LibraryKind>().SelectMany(k => new LibraryAdapter(k).Cards)
            .DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c.FilePath, c => c, StringComparer.OrdinalIgnoreCase);
        return Cards.Where(c => live.ContainsKey(c.FilePath)).Select(c => live[c.FilePath]).ToArray();
    }
    public void Open(Frame frame, CardBase card, bool replace = false)
    {
        ReturnPath = card.FilePath;
        var page = card switch
        {
            CharacterCard => typeof(CharacterDetailPage), CoordinateCard => typeof(CoordinateDetailPage),
            MediaCard => typeof(ScreenshotDetailPage), _ => typeof(DetailPage)
        };
        frame.Navigate(page, new BrowseNavigation(card, this));
        if (replace && frame.BackStack.Count > 0) frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
    }
}

public sealed record BrowseNavigation(CardBase Card, BrowseContext Context);

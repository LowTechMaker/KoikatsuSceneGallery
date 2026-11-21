using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;
using KoikatsuSceneGallery.Pages;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Models;

public sealed class BrowseContext(string title, IEnumerable<CardBase> cards, bool showRelatedImages = false)
{
    public string Title { get; } = title;
    public bool ShowRelatedImages { get; } = showRelatedImages;
    public IReadOnlyList<CardBase> Cards { get; } = cards.DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase).ToArray();
    public string? ReturnPath { get; set; }
    /// <summary>
    /// For a folder opened from the gallery: the gallery's own list, which the
    /// filmstrip shows around the folder (darkened) and opens into.
    /// </summary>
    public BrowseContext? Outer { get; init; }
    public IReadOnlyList<CardBase> CurrentCards()
    {
        var live = App.Services.GetRequiredService<LibraryRegistry>().All.SelectMany(library => library.Cards)
            .DistinctBy(c => c.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(c => c.FilePath, c => c, StringComparer.OrdinalIgnoreCase);
        return Cards.Where(c => live.ContainsKey(c.FilePath)).Select(c => live[c.FilePath]).ToArray();
    }
    public void Open(Frame frame, CardBase card, bool replace = false)
    {
        ReturnPath = card.FilePath;
        // One viewer for every kind (wave 15): the loupe and the details pane.
        frame.Navigate(typeof(CardViewerPage), new BrowseNavigation(card, this));
        if (replace && frame.BackStack.Count > 0) frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
    }
}

public sealed record BrowseNavigation(CardBase Card, BrowseContext Context);

using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>One row of the home page.</summary>
internal sealed class HomeShelf(string key, string title, string emptyText)
{
    /// <summary>Stable name, used for automation ids and to tell shelves apart in code.</summary>
    public string Key { get; } = key;
    public string Title { get; } = title;
    /// <summary>Shown instead of the row when it has no cards; empty hides the shelf.</summary>
    public string EmptyText { get; } = emptyText;
    public ObservableCollection<CardBase> Cards { get; } = [];
}

/// <summary>
/// The home page's shelves: what the user queued, a hand dealt at random, what
/// they used lately and what they like. Scenes only — the home page answers
/// "which scene do I play now", and characters are picked after that.
/// </summary>
/// <remarks>
/// Holds paths, never card objects, between refreshes: every scan replaces the
/// gallery's card objects, so a shelf built from old ones would show cards
/// whose thumbnails never arrive.
/// </remarks>
internal sealed class HomeViewModel
{
    public const string QueuedKey = "queued";
    public const string DealKey = "deal";
    public const string RecentKey = "recent";
    public const string LikedKey = "liked";

    /// <summary>How many cards a state shelf shows before "see all" is the way to the rest.</summary>
    public const int ShelfLimit = 30;

    private readonly ILibrary _scenes;
    private readonly CardStateService _states;
    private readonly HomeDeal _dealer = new(Random.Shared);
    private string[] _dealt = [];

    public HomeViewModel(LibraryRegistry libraries, CardStateService states)
    {
        _scenes = libraries.Get(LibraryKind.Scenes);
        _states = states;
        Queued = new(QueuedKey, UiText.Get("Home_Queued"), UiText.Get("Home_QueuedEmpty"));
        Deal = new(DealKey, UiText.Get("Home_Deal"), "");
        Recent = new(RecentKey, UiText.Get("Home_Recent"), "");
        Liked = new(LikedKey, UiText.Get("Home_Liked"), "");
        Shelves = [Queued, Deal, Recent, Liked];
    }

    public HomeShelf Queued { get; }
    public HomeShelf Deal { get; }
    public HomeShelf Recent { get; }
    public HomeShelf Liked { get; }
    public IReadOnlyList<HomeShelf> Shelves { get; }

    /// <summary>The scene library's cards by path, as they are right now.</summary>
    private Dictionary<string, CardBase> Live() => _scenes.Cards
        .DistinctBy(card => card.FilePath, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(card => card.FilePath, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rebuilds the three state shelves from the store and re-resolves the
    /// dealt hand against the current card objects. Call on the UI thread.
    /// </summary>
    public void Refresh()
    {
        var live = Live();
        var states = _states.Snapshot()
            .Where(entry => live.ContainsKey(entry.FullPath))
            .ToArray();

        Replace(Queued, states
            .Where(entry => entry.State.Queued)
            .OrderByDescending(entry => entry.State.UpdatedAt)
            .Select(entry => live[entry.FullPath]));
        Replace(Recent, states
            .Where(entry => entry.State is { LastUsedAt: not null, Hidden: false })
            .OrderByDescending(entry => entry.State.LastUsedAt)
            .Select(entry => live[entry.FullPath]));
        Replace(Liked, states
            .Where(entry => entry.State.Liked)
            .OrderByDescending(entry => entry.State.UpdatedAt)
            .Select(entry => live[entry.FullPath]));

        if (_dealt.Length == 0) DealAgain();
        // A card hidden from the hand itself leaves it at once; the hand is not
        // redealt, so the rest of it stays where the user was looking.
        else Replace(Deal, _dealt
            .Where(path => live.ContainsKey(path) && !_states.Has(path, CardFlag.Hidden))
            .Select(path => live[path]));
    }

    /// <summary>
    /// Deals a fresh hand, leaving out hidden cards, the way the discovery page
    /// deals a poker hand (<see cref="HomeDeal"/>).
    /// </summary>
    public void DealAgain()
    {
        var candidates = _scenes.Cards
            .Where(card => !_states.Has(card.FilePath, CardFlag.Hidden))
            .ToArray();
        var hand = _dealer.Deal(candidates, _dealt, card => card.FilePath, card => card.FileSize);
        _dealt = [.. hand.Select(card => card.FilePath)];
        Replace(Deal, hand);
    }

    private static void Replace(HomeShelf shelf, IEnumerable<CardBase> cards)
    {
        var wanted = cards.Take(ShelfLimit).ToArray();
        // Untouched when nothing changed: resetting the collection would drop
        // and reload every tile's thumbnail.
        if (shelf.Cards.SequenceEqual(wanted)) return;
        shelf.Cards.Clear();
        foreach (var card in wanted) shelf.Cards.Add(card);
    }
}

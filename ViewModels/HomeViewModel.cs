using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>What a home page tile says about a card.</summary>
/// <param name="Author">The author's name, or null when unknown.</param>
/// <param name="AuthorOthers">The author's other scene cards.</param>
/// <param name="PostOthers">The post's other scene cards.</param>
/// <param name="PostKey">The card's grouping key; <c>post:…</c> when it belongs to a post.</param>
internal sealed record HomeCardInfo(
    string Title, string? Author, IReadOnlyList<string> Tags,
    int AuthorOthers, int PostOthers, string FileName, string? PostKey);

/// <summary>One row of the home page.</summary>
internal sealed class HomeShelf(string key, string title, string emptyText)
{
    /// <summary>Stable name, used for automation ids and to tell shelves apart in code.</summary>
    public string Key { get; } = key;
    public string Title { get; private set; } = title;

    /// <summary>Raised when a themed shelf gets another theme (and title).</summary>
    public event Action? TitleChanged;

    /// <summary>The theme a themed shelf shows today, or null.</summary>
    public ThemeKind? Theme { get; private set; }

    /// <summary>What a themed shelf's "see all" opens: the author key; null when there is nothing to open.</summary>
    public string? SubjectKey { get; private set; }

    public void SetTheme(string title, ThemeKind? theme, string? subjectKey)
    {
        var changed = title != Title || theme != Theme || subjectKey != SubjectKey;
        Title = title;
        Theme = theme;
        SubjectKey = subjectKey;
        if (changed) TitleChanged?.Invoke();
    }
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
    private readonly SceneRatingResolver? _posts;
    private readonly AuthorInfoService? _authors;
    private readonly HomeDeal _dealer = new(Random.Shared);
    private string[] _dealt = [];

    // Per load, built on first use: card path → post key, post key → scene
    // cards in it, author → scene cards by them. Dropped when the library or
    // the sidecars change; never read from a file.
    private Dictionary<string, string>? _postKeyByPath;
    private Dictionary<string, int>? _postCounts;
    private Dictionary<AuthorKey, int>? _authorCounts;
    private volatile bool _indexStale = true;

    public HomeViewModel(LibraryRegistry libraries, CardStateService states,
        SceneRatingResolver? posts = null, AuthorInfoService? authors = null)
    {
        _scenes = libraries.Get(LibraryKind.Scenes);
        _states = states;
        _posts = posts;
        _authors = authors;
        // Themes are chosen again when the library or the sidecars change, never
        // on a state change: flagging a card must not reshuffle the page.
        _scenes.CardsChanged += (_, _) =>
        {
            _indexStale = true;
            _themesStale = true;
        };
        if (posts is not null)
        {
            // Raised off the UI thread once the sidecars were read: titles may
            // have arrived for cards already on screen, and tags for the themes.
            posts.PostsReloaded += () =>
            {
                _indexStale = true;
                _themesStale = true;
                CardInfoChanged?.Invoke();
            };
        }
        Queued = new(QueuedKey, UiText.Get("Home_Queued"), UiText.Get("Home_QueuedEmpty"));
        Deal = new(DealKey, UiText.Get("Home_Deal"), "");
        Recent = new(RecentKey, UiText.Get("Home_Recent"), "");
        Liked = new(LikedKey, UiText.Get("Home_Liked"), "");
        Themes = [.. Enumerable.Range(0, ThemeCount).Select(i => new HomeShelf(ThemeKeyPrefix + i, "", ""))];
        Shelves = [Queued, .. Themes, Deal, Recent, Liked];
    }

    // ------------------------------------------------------------ themed shelves

    public const string ThemeKeyPrefix = "theme";
    public const int ThemeCount = 3;

    private volatile bool _themesStale = true;
    private DateOnly _themesDay;
    private int _reroll;
    private string[][] _themePaths = [];

    /// <summary>Today's themed shelves (<see cref="HomeThemes"/>), between "want to try" and the dealt hand.</summary>
    public IReadOnlyList<HomeShelf> Themes { get; }

    /// <summary>Other themes for today. The count is reset when the date changes.</summary>
    public void RerollThemes()
    {
        _reroll++;
        _themesStale = true;
        Refresh();
    }

    /// <summary>The author an <see cref="ThemeKind.Author"/> shelf is about, for its "see all".</summary>
    public AuthorSummary? AuthorFor(string subjectKey)
        => _authors?.GetSummaries().FirstOrDefault(s => AuthorId(s.Display.Key) == subjectKey);

    private static string AuthorId(AuthorKey key) => $"{key.ProviderId}\u001F{key.Id}";

    private void RefreshThemes(Dictionary<string, CardBase> live)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (today != _themesDay)
        {
            // A new day while the app is open: new themes, and rerolls count from zero.
            _themesDay = today;
            _reroll = 0;
            _themesStale = true;
        }
        if (_themesStale)
        {
            _themesStale = false;
            ChooseThemes(live, today);
        }
        // Between choices only the cards follow: one that became hidden or was
        // removed leaves its shelf, nothing is reshuffled.
        for (var i = 0; i < Themes.Count; i++)
        {
            string[] paths = i < _themePaths.Length ? _themePaths[i] : [];
            Replace(Themes[i], paths
                .Where(path => live.ContainsKey(path) && !_states.Has(path, CardFlag.Hidden))
                .Select(path => live[path]));
        }
    }

    private void ChooseThemes(Dictionary<string, CardBase> live, DateOnly today)
    {
        EnsureIndex();
        var facts = live.Values.Select(card =>
        {
            var state = _states.Get(card.FilePath);
            var author = (card as IAuthorOwner)?.Author;
            var postKey = _postKeyByPath!.GetValueOrDefault(card.FilePath);
            IReadOnlyList<string> tags = postKey is null ? [] : HomeCardText.Tags(_posts?.PostInfo(postKey)?.Tags ?? [], int.MaxValue);
            return new ThemeCard(
                card.FilePath,
                author is null ? null : AuthorId(author.Key),
                author?.Name,
                postKey is not null && postKey.StartsWith("post:", StringComparison.Ordinal) ? postKey : null,
                tags,
                state?.LastUsedAt is not null,
                state?.LastUsedAt,
                state?.Liked == true,
                state?.Hidden == true,
                card.DateCreated);
        }).ToList();

        // SimilarToLast is not offered: the library has no fingerprints to read
        // cheaply (see the wave 5 report), so no fingerprint function is passed.
        var chosen = HomeThemes.Pick(facts, today, _reroll, ThemeCount);
        _themePaths = [.. chosen.Select(shelf => shelf.CardPaths.ToArray())];
        for (var i = 0; i < Themes.Count; i++)
        {
            if (i >= chosen.Count)
            {
                Themes[i].SetTheme("", null, null);
                continue;
            }
            var shelf = chosen[i];
            var title = shelf.Kind switch
            {
                ThemeKind.Author => UiText.Format("Home_ThemeAuthor", shelf.Subject ?? ""),
                ThemeKind.Tag => UiText.Format("Home_ThemeTag", shelf.Subject ?? ""),
                ThemeKind.NeverPlayed => UiText.Get("Home_ThemeNeverPlayed"),
                ThemeKind.DustyFavourites => UiText.Get("Home_ThemeDusty"),
                _ => UiText.Format("Home_ThemeSimilar",
                    shelf.Subject is { } path && live.TryGetValue(path, out var card) ? Describe(card).Title : ""),
            };
            // Only an author has a page to open; a tag cannot be opened by name (see the report).
            Themes[i].SetTheme(title, shelf.Kind, shelf.Kind == ThemeKind.Author ? shelf.SubjectKey : null);
        }
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
        RefreshThemes(live);

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

    // ------------------------------------------------------------ what a tile says

    /// <summary>Raised, possibly off the UI thread, when titles or counts may have changed.</summary>
    public event Action? CardInfoChanged;

    /// <summary>
    /// What a tile says about a card: the post title (else the artwork folder's,
    /// else the file name), the author, a few tags, and how many other scene
    /// cards the author and the post have. Call on the UI thread; no file is read.
    /// </summary>
    public HomeCardInfo Describe(CardBase card)
    {
        EnsureIndex();
        var postKey = _postKeyByPath!.GetValueOrDefault(card.FilePath);
        var post = postKey is null ? null : _posts?.PostInfo(postKey);
        var author = (card as IAuthorOwner)?.Author;
        var title = HomeCardText.Title(post?.Title, card.FileName,
            Path.GetFileName(Path.GetDirectoryName(card.FilePath)),
            _posts?.FolderArtworkId(card), author?.Key.Id);
        var authorOthers = author is null ? 0 : Math.Max(0, _authorCounts!.GetValueOrDefault(author.Key) - 1);
        var postOthers = postKey is not null && postKey.StartsWith("post:", StringComparison.Ordinal)
            ? Math.Max(0, _postCounts!.GetValueOrDefault(postKey) - 1)
            : 0;
        return new HomeCardInfo(title, author?.Name, HomeCardText.Tags(post?.Tags ?? [], 5),
            authorOthers, postOthers, card.FileName, postKey);
    }

    private void EnsureIndex()
    {
        if (!_indexStale && _postKeyByPath is not null) return;
        _indexStale = false;
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var posts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in _scenes.Cards)
        {
            if (_posts?.PostKeyOf(card) is not { } key) continue;
            if (!keys.TryAdd(card.FilePath, key)) continue;
            posts[key] = posts.GetValueOrDefault(key) + 1;
        }
        _postKeyByPath = keys;
        _postCounts = posts;
        _authorCounts = _authors?.GetSummaries().ToDictionary(s => s.Display.Key, s => s.SceneCount) ?? [];
    }

    // ------------------------------------------------------------ this or that

    private readonly HomeDuel _duel = new(Random.Shared);
    private (string Left, string Right)? _current;
    private (string Left, string Right)? _upcoming;

    /// <summary>Cards picked into "want to try" this session.</summary>
    public int DuelPicked => _duel.Picked;

    /// <summary>
    /// The pair to show now, and the one after it (to preload), as current card
    /// objects; null when fewer than two cards can be shown. Hidden and queued
    /// cards never appear; a pair that became invalid since is replaced.
    /// </summary>
    public ((CardBase Left, CardBase Right)? Current, (CardBase Left, CardBase Right)? Upcoming) DuelPairs()
    {
        var live = Live();
        if (!Valid(_current, live)) _current = NextPair(live);
        if (!Valid(_upcoming, live)) _upcoming = NextPair(live);
        return (Resolve(_current, live), Resolve(_upcoming, live));
    }

    /// <summary>Counts a pick (the caller has queued the card) and moves to the next pair.</summary>
    public void DuelPick()
    {
        _duel.Pick();
        DuelSkip();
    }

    /// <summary>Moves to the next pair without choosing; the pair just shown is not shown again this session.</summary>
    public void DuelSkip()
    {
        _current = _upcoming;
        _upcoming = null;
    }

    private bool Valid((string Left, string Right)? pair, Dictionary<string, CardBase> live)
        => pair is { } p && Eligible(p.Left, live) && Eligible(p.Right, live);

    private bool Eligible(string path, Dictionary<string, CardBase> live)
        => live.ContainsKey(path) && !_states.Has(path, CardFlag.Hidden) && !_states.Has(path, CardFlag.Queued);

    private (string, string)? NextPair(Dictionary<string, CardBase> live)
    {
        EnsureIndex();
        var cards = live.Values.Select(card => new DuelCard(
            card.FilePath,
            _postKeyByPath!.GetValueOrDefault(card.FilePath) is { } key && key.StartsWith("post:", StringComparison.Ordinal) ? key : null,
            (card as IAuthorOwner)?.Author?.Key.ToString(),
            _states.Get(card.FilePath)?.LastUsedAt is not null)).ToList();
        // The pair already waiting is not offered again as the one after it.
        var waiting = _current;
        var pair = _duel.Next(cards, path =>
            !Eligible(path, live)
            || waiting is { } w && (string.Equals(w.Left, path, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(w.Right, path, StringComparison.OrdinalIgnoreCase)));
        return pair is null ? null : (pair.Left.Path, pair.Right.Path);
    }

    private static (CardBase, CardBase)? Resolve((string Left, string Right)? pair, Dictionary<string, CardBase> live)
        => pair is { } p && live.TryGetValue(p.Left, out var left) && live.TryGetValue(p.Right, out var right)
            ? (left, right) : null;

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

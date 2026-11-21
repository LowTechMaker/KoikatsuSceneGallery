namespace KoikatsuSceneGallery.Helpers;

/// <summary>A scene card as the home page's "N more" counts see it.</summary>
/// <param name="Counts">Whether the user would find this card if they went to look: it passes the
/// rating filter, and is not "not for me" unless hidden cards are being shown.</param>
internal sealed record CountedCard(string Path, string? AuthorKey, string? PostKey, bool Counts);

/// <summary>
/// "N more by this author" and "N more from the same post" on the home page's
/// tiles: the other cards the user would actually find on the author's or the
/// post's page, so cards the rating filter or "not for me" leaves out are not
/// counted. Built once per refresh from the library in memory; no file is read.
/// </summary>
internal sealed class HomeCardCounts
{
    private readonly HashSet<string> _counted;
    private readonly Dictionary<string, int> _byAuthor;
    private readonly Dictionary<string, int> _byPost;

    private HomeCardCounts(HashSet<string> counted, Dictionary<string, int> byAuthor, Dictionary<string, int> byPost)
    {
        _counted = counted;
        _byAuthor = byAuthor;
        _byPost = byPost;
    }

    public static HomeCardCounts Empty { get; } = Build([]);

    public static HomeCardCounts Build(IEnumerable<CountedCard> cards)
    {
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byAuthor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byPost = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in cards)
        {
            if (!card.Counts || !seen.Add(card.Path)) continue;
            counted.Add(card.Path);
            if (card.AuthorKey is { } author) byAuthor[author] = byAuthor.GetValueOrDefault(author) + 1;
            if (IsPost(card.PostKey)) byPost[card.PostKey!] = byPost.GetValueOrDefault(card.PostKey!) + 1;
        }
        return new HomeCardCounts(counted, byAuthor, byPost);
    }

    /// <summary>Only a real post (<c>post:…</c>) is "the same post"; a folder or file key is not.</summary>
    private static bool IsPost(string? key) => key is not null && key.StartsWith("post:", StringComparison.OrdinalIgnoreCase);

    public int AuthorOthers(string path, string? authorKey)
        => authorKey is null ? 0 : Others(_byAuthor.GetValueOrDefault(authorKey), path);

    public int PostOthers(string path, string? postKey)
        => !IsPost(postKey) ? 0 : Others(_byPost.GetValueOrDefault(postKey!), path);

    private int Others(int total, string path) => Math.Max(0, total - (_counted.Contains(path) ? 1 : 0));

    /// <summary>Whether <paramref name="other"/> gives every card the same numbers (so the tiles need no redraw).</summary>
    public bool SameAs(HomeCardCounts? other)
        => other is not null
           && _counted.SetEquals(other._counted)
           && Same(_byAuthor, other._byAuthor)
           && Same(_byPost, other._byPost);

    private static bool Same(Dictionary<string, int> a, Dictionary<string, int> b)
        => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var n) && n == p.Value);
}

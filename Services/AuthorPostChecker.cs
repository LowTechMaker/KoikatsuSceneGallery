using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>One post of the author that lists at least one of the dropped cards.</summary>
internal sealed record AuthorPostCheckEntry(ArtworkId Post, string? Title, PostLinkMatch Match)
{
    /// <summary>
    /// The dropped cards that belong to this post: those it lists that no post listing fewer files
    /// lists too. An author's posts overlap: one links the whole archive, another a single series,
    /// and a card is in all of them; it belongs to the most specific. Null while the check is
    /// running (an entry reported as progress does not know yet); an empty list afterwards means
    /// every card this post lists belongs to a more specific post, so it is a collection.
    /// </summary>
    public IReadOnlyList<string>? ClaimedCards { get; init; }

    public bool IsCollection => ClaimedCards is { Count: 0 };
}

/// <summary>Where a check stands. <see cref="Entry"/> is set when the post just looked at is one of the author's that matters.</summary>
internal sealed record AuthorCheckProgress(int Done, int Total, AuthorPostCheckEntry? Entry);

internal enum AuthorCheckStatus
{
    Completed,

    /// <summary>The author's posts could not be listed: unknown author, offline, or refused.</summary>
    ListFailed,
}

/// <param name="Checked">Posts that were fetched and examined.</param>
/// <param name="Unreadable">Posts that could not be fetched (deleted, private, or the request failed).</param>
/// <param name="WithoutLinks">Posts whose description has no link whose files could be read.</param>
/// <param name="Entries">The posts that list at least one dropped card, newest first.</param>
/// <param name="UnclaimedCards">Dropped cards that no post of this author lists.</param>
/// <param name="StoppedEarly">
/// True when the run ended before the oldest post because every dropped card had already been
/// claimed: older posts could not change the answer, and each one costs a rate-limited request.
/// </param>
internal sealed record AuthorCheckResult(
    AuthorCheckStatus Status,
    int Total,
    int Checked,
    int Unreadable,
    int WithoutLinks,
    IReadOnlyList<AuthorPostCheckEntry> Entries,
    IReadOnlyList<string> UnclaimedCards,
    bool StoppedEarly = false);

/// <summary>
/// Goes through an author's posts one by one, newest first, and compares the files each
/// post's links list with the cards the user has dropped in. Each post costs a rate-limited
/// request, so progress is reported after every one and the run can be cancelled; a post
/// that cannot be read is counted and skipped, never fatal.
/// </summary>
internal sealed class AuthorPostChecker
{
    private readonly Func<AuthorKey, CancellationToken, Task<IReadOnlyList<ArtworkId>?>> _listPosts;
    private readonly Func<ArtworkId, CancellationToken, Task<ArtworkInfo?>> _fetchPost;
    private readonly Func<string?, CancellationToken, Task<IReadOnlyList<PostLinkInfo>>> _inspectLinks;
    private readonly int _gracePosts;

    internal const int DefaultGracePosts = 8;

    /// <param name="gracePosts">
    /// How many more posts to look at after every card has been claimed. The first post to claim a
    /// card is not necessarily its home: a post linking the whole archive is claimed by everything,
    /// and the specific posts that really own the cards come after it.
    /// </param>
    internal AuthorPostChecker(
        Func<AuthorKey, CancellationToken, Task<IReadOnlyList<ArtworkId>?>> listPosts,
        Func<ArtworkId, CancellationToken, Task<ArtworkInfo?>> fetchPost,
        Func<string?, CancellationToken, Task<IReadOnlyList<PostLinkInfo>>> inspectLinks,
        int gracePosts = DefaultGracePosts)
    {
        _gracePosts = gracePosts;
        _listPosts = listPosts;
        _fetchPost = fetchPost;
        _inspectLinks = inspectLinks;
    }

    /// <param name="cardNames">File names (without folders) of the cards in the import list.</param>
    /// <param name="libraryHas">Whether the library already holds a file of that name and size; null if unknown.</param>
    internal async Task<AuthorCheckResult> CheckAsync(
        AuthorKey author,
        IReadOnlyList<string> cardNames,
        Func<string, long?, bool>? libraryHas,
        IProgress<AuthorCheckProgress>? progress,
        CancellationToken ct)
    {
        var posts = await _listPosts(author, ct).ConfigureAwait(false);
        if (posts is null)
            return new AuthorCheckResult(AuthorCheckStatus.ListFailed, 0, 0, 0, 0, [], cardNames);

        var entries = new List<AuthorPostCheckEntry>();
        var everyCard = new HashSet<string>(cardNames, StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stoppedEarly = false;
        var sinceLastMatch = 0;
        int examined = 0, unreadable = 0, withoutLinks = 0, done = 0;
        foreach (var post in posts)
        {
            ct.ThrowIfCancellationRequested();
            AuthorPostCheckEntry? entry = null;
            sinceLastMatch++;

            ArtworkInfo? info = null;
            try { info = await _fetchPost(post, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { /* counted below: one bad post must not end the run */ }

            if (info is null) unreadable++;
            else
            {
                examined++;
                var links = await _inspectLinks(info.Description, ct).ConfigureAwait(false);
                var listed = links.OrderBy(link => link.IsFolder).SelectMany(link => link.Files).Select(file => new ListedFile(file.Name, file.Size, file.Url, file.Folder, file.FolderUrl)).ToList();
                if (listed.Count == 0) withoutLinks++;
                else
                {
                    var match = PostLinkMatcher.MatchFiles(listed, cardNames, libraryHas);
                    if (match.Matched.Count > 0)
                    {
                        entry = new AuthorPostCheckEntry(post, info.Title, match);
                        entries.Add(entry);
                        claimed.UnionWith(match.Matched);
                        sinceLastMatch = 0;
                    }
                }
            }

            progress?.Report(new AuthorCheckProgress(++done, posts.Count, entry));

            // Every card has a post and the last few posts added nothing: what is older is very
            // unlikely to be a better home for any of them.
            if (everyCard.Count > 0 && claimed.Count >= everyCard.Count
                && sinceLastMatch >= _gracePosts && done < posts.Count)
            {
                stoppedEarly = true;
                break;
            }
        }

        var unclaimed = cardNames.Where(name => !claimed.Contains(name)).ToList();
        return new AuthorCheckResult(
            AuthorCheckStatus.Completed, posts.Count, examined, unreadable, withoutLinks,
            AssignCardsToMostSpecificPost(entries), unclaimed, stoppedEarly);
    }

    // A card listed by several posts belongs to the one that lists the fewest files: a post about
    // one work beats the post that links everything. On a tie the newer post wins, being met first.
    internal static IReadOnlyList<AuthorPostCheckEntry> AssignCardsToMostSpecificPost(
        IReadOnlyList<AuthorPostCheckEntry> entries)
    {
        var owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
            foreach (var card in entries[i].Match.Matched)
                if (!owner.TryGetValue(card, out var current)
                    || entries[i].Match.ListedCount < entries[current].Match.ListedCount)
                    owner[card] = i;

        return entries
            .Select((entry, index) => entry with
            {
                ClaimedCards = entry.Match.Matched.Where(card => owner[card] == index).ToList(),
            })
            .ToList();
    }
}

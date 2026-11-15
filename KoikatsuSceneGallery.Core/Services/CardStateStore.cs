using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>The three things the user can say about a card.</summary>
internal enum CardFlag
{
    /// <summary>"I want to try this one."</summary>
    Queued,
    /// <summary>"I like this one."</summary>
    Liked,
    /// <summary>"Not for me" — hidden from browsing, never deleted.</summary>
    Hidden,
}

/// <summary>
/// The user's relationship with one card, as opposed to facts about the card
/// (rating, author, game), which are read from elsewhere.
/// </summary>
/// <param name="Hash">The card's <see cref="CardPayloadHash"/>, its identity.</param>
/// <param name="RelativePath">Where the card was last seen, relative to its library root.</param>
/// <param name="LastUsedAt">When the card was last dragged or copied out of the app.</param>
internal sealed record CardState(
    string Hash,
    string RelativePath,
    bool Queued,
    bool Liked,
    bool Hidden,
    DateTimeOffset? LastUsedAt,
    int UseCount,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Nothing worth keeping on disk: no flag and never used.</summary>
    public bool IsEmpty => !Queued && !Liked && !Hidden && LastUsedAt is null;

    public bool Has(CardFlag flag) => flag switch
    {
        CardFlag.Queued => Queued,
        CardFlag.Liked => Liked,
        CardFlag.Hidden => Hidden,
        _ => false,
    };
}

internal sealed record CardStateDocument(int SchemaVersion, IReadOnlyList<CardState> States);

/// <summary>
/// What the user has said about cards and which ones they have used, one file
/// per library root at <c>&lt;root&gt;\.scenegallery\card_states.json</c>.
/// </summary>
/// <remarks>
/// Built the same way as <see cref="UserCardRatingStore"/>, for the same
/// reasons: a card is identified by the hash of its card data so a move or
/// rename keeps its state, the stored path is only the cheap lookup, and the
/// file travels with the library. Read that class before changing this one.
///
/// Rules the store enforces, so no caller has to:
/// <list type="bullet">
/// <item>Setting <see cref="CardFlag.Hidden"/> clears <see cref="CardFlag.Queued"/>.</item>
/// <item>Setting <see cref="CardFlag.Queued"/> or <see cref="CardFlag.Liked"/> clears <see cref="CardFlag.Hidden"/>.</item>
/// <item>Using a card clears <see cref="CardFlag.Queued"/>: it has been tried.</item>
/// <item>An entry that became <see cref="CardState.IsEmpty"/> is removed, not written.</item>
/// </list>
///
/// Unlike the rating store, this one is loaded with the roots of all three
/// libraries at once, and <see cref="EnsureLoaded"/> must not drop roots a
/// previous caller asked for: several pages call it with different sets.
/// </remarks>
internal sealed class CardStateStore
{
    public const string FileName = "card_states.json";
    public const int CurrentSchemaVersion = 1;

    public CardStateStore(Func<DateTimeOffset>? now = null)
    {
        _ = now;
    }

    /// <summary>
    /// Raised with the card's full path after its state changed. Fires on
    /// whatever thread did the write, after the file is on disk.
    /// </summary>
#pragma warning disable CS0067 // raised by the implementation
    public event Action<string>? StateChanged;
#pragma warning restore CS0067

    public static string GetFilePath(string root)
        => Path.Combine(Path.GetFullPath(root), LibraryMetadataDirectory.Name, FileName);

    /// <summary>
    /// Makes sure the state files of <paramref name="roots"/> are loaded.
    /// Roots already loaded stay loaded; only new ones are read.
    /// </summary>
    public void EnsureLoaded(IEnumerable<string> roots) => throw new NotImplementedException();

    /// <summary>The state last stored for a card at this path, without hashing it.</summary>
    public CardState? TryGetByPath(string fullPath) => throw new NotImplementedException();

    /// <summary>Every stored state with the full path it was last seen at.</summary>
    public IReadOnlyList<(string FullPath, CardState State)> Snapshot() => throw new NotImplementedException();

    /// <summary>
    /// The state of the card at <paramref name="fullPath"/> with this hash.
    /// When it was stored under another path (moved or renamed), the stored
    /// path is updated so the next lookup is by path again.
    /// </summary>
    public Task<CardState?> ResolveAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>
    /// Sets or clears one flag. False when the card is outside every loaded
    /// root or its root's file could not be read this session.
    /// </summary>
    public Task<bool> SetFlagAsync(string fullPath, string hash, CardFlag flag, bool value,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    /// <summary>Records that the card was taken out of the app just now.</summary>
    public Task<bool> MarkUsedAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}

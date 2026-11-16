using System.Text.Json;
using System.Text.Json.Serialization;
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
/// <param name="UpdatedAt">When a flag or a use last changed; orders the state shelves.</param>
/// <param name="FiledRating">
/// The rating the card was filed under (0 = G, 1 = R-18, 2 = R-18G, the
/// <c>SceneRating</c> scale), or null when never recorded. Rating folders used
/// to say this; the simpler library layout has none, so it is kept here
/// before a card leaves the folder that implied it. Not written when null.
/// </param>
internal sealed record CardState(
    string Hash,
    string RelativePath,
    bool Queued,
    bool Liked,
    bool Hidden,
    DateTimeOffset? LastUsedAt,
    int UseCount,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FiledRating = null)
{
    /// <summary>
    /// Nothing worth keeping on disk: no flag, never used, and no rating above
    /// G. A filed G (or none) says nothing G does not already mean by default.
    /// </summary>
    /// <remarks>Derived, so not written: a stored copy could only disagree with the fields.</remarks>
    [JsonIgnore]
    public bool IsEmpty => !Queued && !Liked && !Hidden && LastUsedAt is null && FiledRating is not (1 or 2);

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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, (string Root, CardState Entry)> _byHash = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _hashByPath = new(PathComparison.Comparer);
    private readonly List<string> _roots = [];
    // Roots whose file exists but could not be read (locked, say). Writing
    // there would replace states this session never saw, so they stay read-only.
    private readonly HashSet<string> _readOnlyRoots = new(PathComparison.Comparer);

    public CardStateStore(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>
    /// Raised with the card's full path after its state changed. Fires on
    /// whatever thread did the write, after the file is on disk.
    /// </summary>
    public event Action<string>? StateChanged;

    public static string GetFilePath(string root)
        => Path.Combine(Path.GetFullPath(root), LibraryMetadataDirectory.Name, FileName);

    /// <summary>
    /// Makes sure the state files of <paramref name="roots"/> are loaded.
    /// Roots already loaded stay loaded; only new ones are read.
    /// </summary>
    public void EnsureLoaded(IEnumerable<string> roots)
    {
        var wanted = roots.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath).Distinct(PathComparison.Comparer).ToArray();
        string[] missing;
        lock (_gate)
            missing = [.. wanted.Where(r => !_roots.Contains(r, PathComparison.Comparer))];
        if (missing.Length == 0) return;

        // Read outside the gate: a slow disk must not block lookups the UI makes.
        var read = missing.Select(root => (root, entries: ReadFile(GetFilePath(root), _now))).ToArray();

        lock (_gate)
        {
            foreach (var (root, entries) in read)
            {
                // Another caller may have loaded the same root while this one
                // was reading; merging it twice would be harmless but pointless.
                if (_roots.Contains(root, PathComparison.Comparer)) continue;
                _roots.Add(root);
                if (entries is null)
                {
                    _readOnlyRoots.Add(root);
                    continue;
                }
                foreach (var entry in entries)
                {
                    // A hash stored under two roots keeps the newer, as ratings do.
                    if (_byHash.TryGetValue(entry.Hash, out var known))
                    {
                        if (entry.UpdatedAt <= known.Entry.UpdatedAt) continue;
                        RemovePathOf(entry.Hash);
                    }
                    _byHash[entry.Hash] = (root, entry);
                    _hashByPath[FullPathOf(root, entry)] = entry.Hash;
                }
            }
        }
    }

    /// <summary>The state last stored for a card at this path, without hashing it.</summary>
    public CardState? TryGetByPath(string fullPath)
    {
        lock (_gate)
            return _hashByPath.TryGetValue(Path.GetFullPath(fullPath), out var hash)
                   && _byHash.TryGetValue(hash, out var known) ? known.Entry : null;
    }

    /// <summary>Every stored state with the full path it was last seen at.</summary>
    public IReadOnlyList<(string FullPath, CardState State)> Snapshot()
    {
        lock (_gate)
            return [.. _byHash.Values.Select(v => (FullPathOf(v.Root, v.Entry), v.Entry))];
    }

    /// <summary>The loaded library root holding <paramref name="fullPath"/>, or null when none does.</summary>
    public string? RootOf(string fullPath)
    {
        lock (_gate) return FindRoot(fullPath);
    }

    /// <summary>
    /// The state of the card at <paramref name="fullPath"/> with this hash.
    /// When it was stored under another path (moved or renamed), the stored
    /// path is updated so the next lookup is by path again.
    /// </summary>
    public async Task<CardState?> ResolveAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
    {
        CardState moved;
        lock (_gate)
        {
            if (!_byHash.TryGetValue(hash, out var known)) return null;
            var root = FindRoot(fullPath);
            var relative = root is null ? null : Path.GetRelativePath(root, Path.GetFullPath(fullPath));
            if (root is null || (PathComparison.Comparer.Equals(root, known.Root)
                                 && PathComparison.Comparer.Equals(relative, known.Entry.RelativePath)))
                return known.Entry;
            moved = known.Entry with { RelativePath = relative! };
        }

        // The state itself is unchanged, so a read-only root only costs the
        // path update; the caller still gets the state.
        await StoreAsync(fullPath, moved.Hash, _ => moved, cancellationToken).ConfigureAwait(false);
        return moved;
    }

    /// <summary>
    /// Sets or clears one flag. False when the card is outside every loaded
    /// root or its root's file could not be read this session.
    /// </summary>
    public Task<bool> SetFlagAsync(string fullPath, string hash, CardFlag flag, bool value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var now = _now();
        return StoreAsync(fullPath, hash, state =>
        {
            var next = flag switch
            {
                CardFlag.Queued => state with { Queued = value },
                CardFlag.Liked => state with { Liked = value },
                CardFlag.Hidden => state with { Hidden = value },
                _ => throw new ArgumentOutOfRangeException(nameof(flag)),
            };
            if (value)
            {
                next = flag == CardFlag.Hidden
                    ? next with { Queued = false }
                    : next with { Hidden = false };
            }
            return next with { UpdatedAt = now };
        }, cancellationToken);
    }

    /// <summary>Records that the card was taken out of the app just now.</summary>
    public Task<bool> MarkUsedAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var now = _now();
        return StoreAsync(fullPath, hash,
            state => state with { LastUsedAt = now, UseCount = state.UseCount + 1, Queued = false, UpdatedAt = now },
            cancellationToken);
    }

    /// <summary>
    /// Records the rating the card was filed under, or clears it with null.
    /// Leaves the flags, the use and <see cref="CardState.UpdatedAt"/> alone,
    /// and the flag rules leave it alone. False when the card is outside every
    /// loaded root or its root is read-only.
    /// </summary>
    public Task<bool> SetFiledRatingAsync(string fullPath, string hash, int? rating,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        CheckRating(rating);
        return StoreAsync(fullPath, hash, state => state with { FiledRating = rating }, cancellationToken);
    }

    /// <summary>
    /// <see cref="SetFiledRatingAsync"/> for many cards at once, writing each
    /// root's file once rather than once per card. Returns how many could not
    /// be stored (outside every root, or a read-only root).
    /// </summary>
    public async Task<int> SetFiledRatingsAsync(IReadOnlyList<(string FullPath, string Hash, int Rating)> ratings,
        CancellationToken cancellationToken = default)
    {
        foreach (var (_, hash, rating) in ratings)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(hash);
            CheckRating(rating);
        }

        var failed = 0;
        var touched = new HashSet<string>(PathComparison.Comparer);
        var changed = new List<string>(ratings.Count);
        lock (_gate)
        {
            foreach (var (fullPath, hash, rating) in ratings)
            {
                var path = Path.GetFullPath(fullPath);
                switch (Apply(path, hash, state => state with { FiledRating = rating }, out var root, out var previousRoot))
                {
                    case ApplyResult.Refused:
                        failed++;
                        break;
                    case ApplyResult.Changed:
                        touched.Add(root!);
                        if (previousRoot is not null) touched.Add(previousRoot);
                        changed.Add(path);
                        break;
                }
            }
        }

        foreach (var root in touched)
            await WriteRootAsync(root, cancellationToken).ConfigureAwait(false);
        foreach (var path in changed)
            StateChanged?.Invoke(path);
        return failed;
    }

    /// <summary>How many times a state file was written; for tests.</summary>
    internal int WriteCount => Volatile.Read(ref _writeCount);

    private int _writeCount;

    private static void CheckRating(int? rating)
    {
        if (rating is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(rating), rating, "A rating is 0 (G), 1 (R-18) or 2 (R-18G).");
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the card's state (a blank one when
    /// it has none), stores it under <paramref name="fullPath"/>, writes the
    /// files it touched and raises <see cref="StateChanged"/>.
    /// </summary>
    private async Task<bool> StoreAsync(string fullPath, string hash, Func<CardState, CardState> change,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(fullPath);
        string? root;
        string? previousRoot;
        lock (_gate)
        {
            switch (Apply(path, hash, change, out root, out previousRoot))
            {
                case ApplyResult.Refused: return false;
                case ApplyResult.Unchanged: return true;
            }
        }

        await WriteRootAsync(root!, cancellationToken).ConfigureAwait(false);
        if (previousRoot is not null)
            await WriteRootAsync(previousRoot, cancellationToken).ConfigureAwait(false);
        StateChanged?.Invoke(path);
        return true;
    }

    private enum ApplyResult { Refused, Unchanged, Changed }

    /// <summary>
    /// The in-memory half of a change: which root's file (and which previous
    /// root's, for an entry that moved roots) must be written. Call under the gate.
    /// </summary>
    private ApplyResult Apply(string path, string hash, Func<CardState, CardState> change,
        out string? root, out string? previousRoot)
    {
        previousRoot = null;
        root = FindRoot(path);
        if (root is null || _readOnlyRoots.Contains(root)) return ApplyResult.Refused;
        var relative = Path.GetRelativePath(root, path);
        CardState current;
        if (_byHash.TryGetValue(hash, out var known))
        {
            if (!PathComparison.Comparer.Equals(known.Root, root))
            {
                // Moving the entry rewrites the old root's file too; when
                // that one is read-only the entry would end up in both.
                if (_readOnlyRoots.Contains(known.Root)) return ApplyResult.Refused;
                previousRoot = known.Root;
            }
            current = known.Entry;
        }
        else
        {
            current = new CardState(hash, relative, false, false, false, null, 0, _now());
        }

        var next = change(current) with { Hash = hash, RelativePath = relative };
        if (next.IsEmpty && !_byHash.ContainsKey(hash))
            return ApplyResult.Unchanged; // Clearing what nobody set: nothing to write or announce.

        RemovePathOf(hash);
        if (next.IsEmpty)
        {
            _byHash.Remove(hash);
        }
        else
        {
            _byHash[hash] = (root, next);
            _hashByPath[path] = hash;
        }
        return ApplyResult.Changed;
    }

    private static string FullPathOf(string root, CardState entry)
        => Path.GetFullPath(Path.Combine(root, entry.RelativePath));

    /// <summary>
    /// Forgets the path the entry for <paramref name="hash"/> is stored under.
    /// An entry has exactly one path, its root plus its relative path, so this
    /// is a lookup rather than a scan; a batch of thousands depends on that.
    /// Call under the gate.
    /// </summary>
    private void RemovePathOf(string hash)
    {
        if (_byHash.TryGetValue(hash, out var known))
            _hashByPath.Remove(FullPathOf(known.Root, known.Entry));
    }

    /// <summary>The deepest loaded root holding <paramref name="fullPath"/>. Call under the gate.</summary>
    private string? FindRoot(string fullPath)
    {
        var path = Path.GetFullPath(fullPath);
        return _roots
            .Where(root => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar,
                PathComparison.Comparison))
            .MaxBy(root => root.Length);
    }

    private async Task WriteRootAsync(string root, CancellationToken cancellationToken)
    {
        var path = GetFilePath(root);
        var writeLock = PathWriteLock.For(path);
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Taken inside the file lock, so two quick changes cannot land on
            // disk in the opposite order and leave the older one behind.
            CardState[] entries;
            lock (_gate)
                entries = [.. _byHash.Values
                    .Where(v => PathComparison.Comparer.Equals(v.Root, root))
                    .Select(v => v.Entry)
                    .OrderBy(e => e.RelativePath, PathComparison.Comparer)];

            var metadataDirectory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(metadataDirectory);
            LibraryMetadataDirectory.MarkHiddenOnWindows(metadataDirectory);
            await AtomicJsonFile.WriteAsync(path, new CardStateDocument(CurrentSchemaVersion, entries),
                JsonOptions, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _writeCount);
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>
    /// The entries in <paramref name="path"/>; empty when there is no file, null
    /// when it could not be read at all. A file that reads but is not ours to
    /// parse (damaged, or written by a newer version) is moved aside first, so
    /// the next change starts a fresh file instead of destroying it.
    /// </summary>
    private static IReadOnlyList<CardState>? ReadFile(string path, Func<DateTimeOffset> now)
    {
        CardStateDocument? document;
        try
        {
            if (!File.Exists(path)) return [];
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<CardStateDocument>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            document = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (document is not { SchemaVersion: >= 1 and <= CurrentSchemaVersion, States: not null })
        {
            try
            {
                File.Move(path, $"{path}.unreadable-{now():yyyyMMddHHmmss}");
                return [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        // An entry with nothing in it would only be dropped at the next write;
        // dropping it now keeps Snapshot() to cards that have a state.
        // A rating outside the scale is not trusted to mean anything: it reads
        // as never recorded rather than as some rating it might have meant.
        return [.. document.States
            .Where(e => e is not null)
            .Select(e => e.FiledRating is null or (>= 0 and <= 2) ? e : e with { FiledRating = null })
            .Where(e => !string.IsNullOrWhiteSpace(e.Hash)
                && !string.IsNullOrWhiteSpace(e.RelativePath)
                && e.UseCount >= 0
                && !e.IsEmpty)];
    }
}

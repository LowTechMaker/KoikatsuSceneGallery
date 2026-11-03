using System.Text.Json;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>One card's rating as the user judged it.</summary>
/// <param name="Hash">The card's <see cref="CardPayloadHash"/>, its identity.</param>
/// <param name="Rating">0 = G, 1 = R-18, 2 = R-18G — the platform rating's scale.</param>
/// <param name="RelativePath">Where the card was last seen, relative to its library root.</param>
internal sealed record UserCardRating(string Hash, int Rating, string RelativePath, DateTimeOffset RatedAt);

internal sealed record UserCardRatingDocument(int SchemaVersion, IReadOnlyList<UserCardRating> Ratings);

/// <summary>
/// The ratings the user gave cards by hand, one file per library root.
/// </summary>
/// <remarks>
/// Deliberately separate from the platform rating in the post sidecars: those
/// describe a post and are replaced by every re-fetch, these describe one card
/// and only change when the user rates it again. A card is identified by the
/// hash of its card data, so moving or renaming it keeps its rating; the stored
/// path is only a fast lookup that saves hashing every card to find out which
/// ones are rated. The file lives in the root's <c>.scenegallery</c> folder so
/// it travels with the library, like the sidecars do.
/// </remarks>
internal sealed class UserCardRatingStore
{
    public const string FileName = "user_ratings.json";
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private Dictionary<string, (string Root, UserCardRating Entry)> _byHash = new(StringComparer.Ordinal);
    private Dictionary<string, string> _hashByPath = new(PathComparison.Comparer);
    private string[] _roots = [];
    // Roots whose file exists but could not be read (locked, say). Writing
    // there would replace ratings this session never saw, so they stay read-only.
    private HashSet<string> _readOnlyRoots = new(PathComparison.Comparer);

    public UserCardRatingStore(Func<DateTimeOffset>? now = null) => _now = now ?? (() => DateTimeOffset.Now);

    /// <summary>Raised with the card's full path after its rating was set or cleared.</summary>
    public event Action<string>? RatingChanged;

    public static string GetFilePath(string root)
        => Path.Combine(Path.GetFullPath(root), LibraryMetadataDirectory.Name, FileName);

    /// <summary>
    /// Reads the rating files of <paramref name="roots"/>, unless exactly those
    /// roots are already loaded. A hash rated under two roots keeps the newer.
    /// </summary>
    public void EnsureLoaded(IEnumerable<string> roots)
    {
        var wanted = roots.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath).Distinct(PathComparison.Comparer).ToArray();
        lock (_gate)
        {
            if (wanted.Length == _roots.Length && wanted.All(r => _roots.Contains(r, PathComparison.Comparer)))
                return;
        }

        var byHash = new Dictionary<string, (string Root, UserCardRating Entry)>(StringComparer.Ordinal);
        var readOnly = new HashSet<string>(PathComparison.Comparer);
        foreach (var root in wanted)
        {
            var entries = ReadFile(GetFilePath(root), _now);
            if (entries is null)
            {
                readOnly.Add(root);
                continue;
            }
            foreach (var entry in entries)
            {
                if (!byHash.TryGetValue(entry.Hash, out var known) || entry.RatedAt > known.Entry.RatedAt)
                    byHash[entry.Hash] = (root, entry);
            }
        }

        lock (_gate)
        {
            _roots = wanted;
            _readOnlyRoots = readOnly;
            _byHash = byHash;
            _hashByPath = new(PathComparison.Comparer);
            foreach (var (root, entry) in byHash.Values)
                _hashByPath[Path.GetFullPath(Path.Combine(root, entry.RelativePath))] = entry.Hash;
        }
    }

    public int Count { get { lock (_gate) return _byHash.Count; } }

    /// <summary>The rating last stored for a card at this path, without hashing it.</summary>
    public int? TryGetByPath(string fullPath)
    {
        lock (_gate)
            return _hashByPath.TryGetValue(Path.GetFullPath(fullPath), out var hash)
                   && _byHash.TryGetValue(hash, out var known) ? known.Entry.Rating : null;
    }

    public int? TryGetByHash(string hash)
    {
        lock (_gate)
            return _byHash.TryGetValue(hash, out var known) ? known.Entry.Rating : null;
    }

    /// <summary>
    /// The rating of the card at <paramref name="fullPath"/> with this hash.
    /// When the card was rated under another path (moved or renamed), the
    /// stored path is updated so the next lookup is by path again.
    /// </summary>
    public async Task<int?> ResolveAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
    {
        UserCardRating? moved = null;
        lock (_gate)
        {
            if (!_byHash.TryGetValue(hash, out var known)) return null;
            var root = FindRoot(fullPath);
            var relative = root is null ? null : Path.GetRelativePath(root, Path.GetFullPath(fullPath));
            if (root is null || (PathComparison.Comparer.Equals(root, known.Root)
                                 && PathComparison.Comparer.Equals(relative, known.Entry.RelativePath)))
                return known.Entry.Rating;
            moved = known.Entry with { RelativePath = relative! };
        }

        await SetCoreAsync(fullPath, moved, cancellationToken).ConfigureAwait(false);
        return moved.Rating;
    }

    /// <summary>Stores <paramref name="rating"/> for the card; false when it is outside every library root.</summary>
    public Task<bool> SetAsync(string fullPath, string hash, int rating, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentOutOfRangeException.ThrowIfLessThan(rating, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rating, 2);
        string? root;
        lock (_gate) root = FindRoot(fullPath);
        if (root is null || IsReadOnly(root)) return Task.FromResult(false);
        var entry = new UserCardRating(hash, rating, Path.GetRelativePath(root, Path.GetFullPath(fullPath)), _now());
        return SetCoreAsync(fullPath, entry, cancellationToken);
    }

    /// <summary>Forgets the user's rating of the card with this hash.</summary>
    public async Task<bool> ClearAsync(string fullPath, string hash, CancellationToken cancellationToken = default)
    {
        string root;
        lock (_gate)
        {
            if (!_byHash.TryGetValue(hash, out var known) || _readOnlyRoots.Contains(known.Root)) return false;
            _byHash.Remove(hash);
            root = known.Root;
            RemovePathsOf(hash);
        }

        await WriteRootAsync(root, cancellationToken).ConfigureAwait(false);
        RatingChanged?.Invoke(Path.GetFullPath(fullPath));
        return true;
    }

    private async Task<bool> SetCoreAsync(string fullPath, UserCardRating entry, CancellationToken cancellationToken)
    {
        string? root;
        string? previousRoot = null;
        lock (_gate)
        {
            root = FindRoot(fullPath);
            if (root is null || _readOnlyRoots.Contains(root)) return false;
            if (_byHash.TryGetValue(entry.Hash, out var known) && !PathComparison.Comparer.Equals(known.Root, root))
            {
                if (_readOnlyRoots.Contains(known.Root)) return false;
                previousRoot = known.Root;
            }
            RemovePathsOf(entry.Hash);
            _byHash[entry.Hash] = (root, entry);
            _hashByPath[Path.GetFullPath(fullPath)] = entry.Hash;
        }

        await WriteRootAsync(root, cancellationToken).ConfigureAwait(false);
        if (previousRoot is not null)
            await WriteRootAsync(previousRoot, cancellationToken).ConfigureAwait(false);
        RatingChanged?.Invoke(Path.GetFullPath(fullPath));
        return true;
    }

    private bool IsReadOnly(string root)
    {
        lock (_gate) return _readOnlyRoots.Contains(root);
    }

    private void RemovePathsOf(string hash)
    {
        foreach (var path in _hashByPath.Where(p => p.Value == hash).Select(p => p.Key).ToList())
            _hashByPath.Remove(path);
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
            // Taken inside the file lock, so two quick ratings cannot land on
            // disk in the opposite order and leave the older one behind.
            UserCardRating[] entries;
            lock (_gate)
                entries = [.. _byHash.Values
                    .Where(v => PathComparison.Comparer.Equals(v.Root, root))
                    .Select(v => v.Entry)
                    .OrderBy(e => e.RelativePath, PathComparison.Comparer)];

            var metadataDirectory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(metadataDirectory);
            LibraryMetadataDirectory.MarkHiddenOnWindows(metadataDirectory);
            await AtomicJsonFile.WriteAsync(path, new UserCardRatingDocument(CurrentSchemaVersion, entries),
                JsonOptions, cancellationToken).ConfigureAwait(false);
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
    /// the next rating starts a fresh file instead of destroying it.
    /// </summary>
    private static IReadOnlyList<UserCardRating>? ReadFile(string path, Func<DateTimeOffset> now)
    {
        UserCardRatingDocument? document;
        try
        {
            if (!File.Exists(path)) return [];
            using var stream = File.OpenRead(path);
            document = JsonSerializer.Deserialize<UserCardRatingDocument>(stream, JsonOptions);
        }
        catch (JsonException)
        {
            document = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (document is not { SchemaVersion: >= 1 and <= CurrentSchemaVersion, Ratings: not null })
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

        return [.. document.Ratings.Where(e => e is not null
            && !string.IsNullOrWhiteSpace(e.Hash)
            && !string.IsNullOrWhiteSpace(e.RelativePath)
            && e.Rating is >= 0 and <= 2)];
    }
}

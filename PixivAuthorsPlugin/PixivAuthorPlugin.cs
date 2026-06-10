using System.Collections.Concurrent;
using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.PixivAuthors;

/// <summary>
/// Resolves pixiv author info for folders named like "ArtistName (12345678)".
/// Anonymous web requests only (no account); rate-limited and disk-cached so
/// each author is fetched at most once until a forced refresh.
/// </summary>
public sealed class PixivAuthorPlugin : IFolderAuthorProvider, IDisposable
{
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromSeconds(2);

    private IPluginHost? _host;
    private PixivApiClient? _client;
    private AuthorDiskCache? _cache;
    private string _avatarDirectory = "";

    // Dedupes concurrent fetches: 50 cards of one author trigger one request.
    private readonly ConcurrentDictionary<string, Lazy<Task<AuthorInfo?>>> _inFlight = new();

    public string Name => "Pixiv Authors";

    public string Version => typeof(PixivAuthorPlugin).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public void Initialize(IPluginHost host)
    {
        _host = host;
        _avatarDirectory = Path.Combine(host.StorageDirectory, "avatars");
        _cache = new AuthorDiskCache(host.StorageDirectory, host.Log);
        _client = new PixivApiClient(new RateLimiter(MinRequestInterval), host.Log);
    }

    public ParsedAuthor? TryParseFolderName(string folderName)
        => PixivFolderNameParser.TryParse(folderName);

    public string GetProfileUrl(AuthorKey key) => $"https://www.pixiv.net/users/{key.Id}";

    public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct)
    {
        if (_client is null || _cache is null || key.ProviderId != PixivFolderNameParser.ProviderId)
            return Task.FromResult<AuthorInfo?>(null);

        if (!forceRefresh && _cache.TryGet(key.Id, out var cached))
            return Task.FromResult(ToAuthorInfo(key, cached));

        // Force-refresh must bypass any in-flight non-force fetch.
        if (forceRefresh)
            _inFlight.TryRemove(key.Id, out _);

        var lazy = _inFlight.GetOrAdd(key.Id, _ => new Lazy<Task<AuthorInfo?>>(
            () => FetchAndCacheAsync(key, ct)));
        return lazy.Value;
    }

    private async Task<AuthorInfo?> FetchAndCacheAsync(AuthorKey key, CancellationToken ct)
    {
        try
        {
            var user = await _client!.FetchUserAsync(key.Id, ct).ConfigureAwait(false);
            if (user is null)
            {
                // Deleted/private account: negative-cache so we don't re-query
                // a dead id every launch (the cache applies a TTL to these).
                _cache!.Set(key.Id, new AuthorDiskCache.CachedAuthor(null, null, DateTimeOffset.UtcNow, Failed: true));
                return null;
            }

            string? avatarFile = null;
            if (user.Value.AvatarUrl is { } avatarUrl && !IsDefaultAvatar(avatarUrl))
            {
                avatarFile = await _client.DownloadAvatarAsync(
                    avatarUrl, Path.Combine(_avatarDirectory, key.Id), ct).ConfigureAwait(false);
            }

            var entry = new AuthorDiskCache.CachedAuthor(
                user.Value.Name, avatarFile, DateTimeOffset.UtcNow, Failed: false);
            _cache!.Set(key.Id, entry);
            return ToAuthorInfo(key, entry);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            // Transport failure (offline, blocked): do NOT negative-cache —
            // next launch should try again.
            _host?.Log($"fetch failed for pixiv user {key.Id}: {ex.Message}");
            return null;
        }
        finally
        {
            _inFlight.TryRemove(key.Id, out _);
        }
    }

    private AuthorInfo? ToAuthorInfo(AuthorKey key, AuthorDiskCache.CachedAuthor entry)
    {
        if (entry.Failed || entry.Name is null) return null;
        var avatar = entry.AvatarFile is { } file && File.Exists(file) ? file : null;
        return new AuthorInfo(key, entry.Name, avatar, GetProfileUrl(key), entry.FetchedAt);
    }

    // pixiv serves a placeholder image for users without an avatar; skip
    // downloading it so the UI falls back to its own person glyph.
    private static bool IsDefaultAvatar(string url) => url.Contains("no_profile", StringComparison.Ordinal);

    public void Dispose()
    {
        _cache?.Dispose();
        _client?.Dispose();
    }
}

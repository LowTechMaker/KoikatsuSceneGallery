using System.Net;
using System.Text.Json;
using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.PixivAuthors;

/// <summary>
/// Anonymous client for pixiv's web JSON endpoints. Presents itself as a
/// regular browser (UA + Referer) because both www.pixiv.net/ajax and the
/// i.pximg.net image host reject clients without them. All requests pass
/// through a shared <see cref="RateLimiter"/>.
/// </summary>
internal sealed class PixivApiClient : IDisposable
{
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/137.0.0.0 Safari/537.36";
    private const string Referer = "https://www.pixiv.net/";

    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)];

    private readonly HttpClient _http;
    private readonly RateLimiter _rateLimiter;
    private readonly Action<string> _log;

    public PixivApiClient(RateLimiter rateLimiter, Action<string> log)
    {
        _rateLimiter = rateLimiter;
        _log = log;
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", Referer);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
    }

    /// <summary>
    /// Fetches public profile info. Returns the parsed (name, avatarUrl), or
    /// null with <c>notFound: true</c> when pixiv reports the user as missing
    /// (deleted/private — cacheable), or throws on transport-level failure.
    /// </summary>
    public async Task<(string Name, string? AvatarUrl)?> FetchUserAsync(
        string userId, CancellationToken ct)
    {
        var url = $"https://www.pixiv.net/ajax/user/{userId}?full=1&lang=en";
        using var response = await SendWithRetryAsync(url, "application/json", ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        // The response shape is unofficial and drifts; probe with JsonDocument
        // instead of binding a model so unrelated changes don't break parsing.
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var error) && error.GetBoolean())
        {
            _log($"pixiv user {userId}: API returned error=true");
            return null;
        }
        if (!root.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
            return null;

        var name = body.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return null;

        string? avatarUrl = null;
        if (body.TryGetProperty("imageBig", out var img) && img.ValueKind == JsonValueKind.String)
            avatarUrl = img.GetString();
        else if (body.TryGetProperty("image", out var imgSmall) && imgSmall.ValueKind == JsonValueKind.String)
            avatarUrl = imgSmall.GetString();

        return (name!, avatarUrl);
    }

    /// <summary>
    /// Downloads an avatar to <paramref name="destinationPath"/> (extension is
    /// appended from the URL). Writes via a temp file + move so a half-written
    /// image is never visible to the app. Returns the final path, or null when
    /// the download fails in a non-retryable way (e.g. image gone).
    /// </summary>
    public async Task<string?> DownloadAvatarAsync(
        string avatarUrl, string destinationPathWithoutExtension, CancellationToken ct)
    {
        var extension = Path.GetExtension(new Uri(avatarUrl).AbsolutePath);
        if (string.IsNullOrEmpty(extension) || extension.Length > 5) extension = ".jpg";
        var finalPath = destinationPathWithoutExtension + extension;

        using var response = await SendWithRetryAsync(avatarUrl, "image/*", ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log($"avatar download failed ({(int)response.StatusCode}): {avatarUrl}");
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        var tempPath = finalPath + ".tmp";
        try
        {
            await using (var file = File.Create(tempPath))
                await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { /* best effort */ }
        }
        return finalPath;
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        string url, string accept, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using (await _rateLimiter.AcquireAsync(ct).ConfigureAwait(false))
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Accept", accept);
                var response = await _http.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                                || (int)response.StatusCode >= 500;
                if (!transient || attempt >= RetryDelays.Length)
                    return response;

                _log($"pixiv returned {(int)response.StatusCode}, retrying in {RetryDelays[attempt].TotalSeconds}s: {url}");
                response.Dispose();
            }
            await Task.Delay(RetryDelays[attempt], ct).ConfigureAwait(false);
        }
    }

    public void Dispose() => _http.Dispose();
}

using SceneGallery.PluginSdk;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Asks the installed link inspectors what a post's download links lead to.
/// A link nobody recognises, or one that cannot be read, simply contributes
/// nothing: this is a hint for the import page and must never get in its way.
/// </summary>
internal sealed class PostLinkLookup
{
    private static readonly TimeSpan PerLinkTimeout = AppTiming.PostLinkTimeout;

    private readonly IReadOnlyList<IPostLinkInspector> _inspectors;
    private readonly IAppLogger _logger;

    internal PostLinkLookup(IReadOnlyList<IPostLinkInspector> inspectors, IAppLogger logger)
    {
        _inspectors = inspectors;
        _logger = logger;
    }

    internal bool HasInspectors => _inspectors.Count > 0;

    /// <summary>Everything the inspectors could read from the links in <paramref name="description"/>.</summary>
    internal async Task<IReadOnlyList<PostLinkInfo>> InspectAsync(string? description, CancellationToken ct)
    {
        if (_inspectors.Count == 0) return [];

        var tasks = new List<Task<PostLinkInfo?>>();
        foreach (var url in PostLinkExtractor.Extract(description))
        {
            var inspector = _inspectors.FirstOrDefault(candidate => SafeCanInspect(candidate, url));
            if (inspector is not null) tasks.Add(InspectOneAsync(inspector, url, ct));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OfType<PostLinkInfo>().ToList();
    }

    private bool SafeCanInspect(IPostLinkInspector inspector, string url)
    {
        try { return inspector.CanInspect(url); }
        catch (Exception ex)
        {
            _logger.LogError("PostLink.CanInspect", ex);
            return false;
        }
    }

    private async Task<PostLinkInfo?> InspectOneAsync(IPostLinkInspector inspector, string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PerLinkTimeout);
        try
        {
            return await inspector.InspectAsync(url, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null; // This link took too long; the rest still count.
        }
        catch (Exception ex)
        {
            _logger.LogError("PostLink.Inspect", ex);
            return null;
        }
    }
}

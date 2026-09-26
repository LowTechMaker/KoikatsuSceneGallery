using KoikatsuSceneGallery.Helpers;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The app's side of <see cref="ITagDictionaryProvider"/>: it asks, the plugin
/// answers. No network access, no parsing and no cache live here — a platform
/// without a plugin simply has no dictionary, and that is the whole rule.
/// </summary>
internal sealed class TagDictionaryService(PluginService plugins, IAppLogger logger)
{
    /// <summary>
    /// Whether this platform can describe its own tags. False for every
    /// platform whose plugin is missing, and for plugins that have no
    /// encyclopedia to offer.
    /// </summary>
    public bool IsAvailableFor(string providerId) => plugins.TagDictionaryFor(providerId) is not null;

    /// <summary>
    /// The language to ask for: the one the UI actually resolved to, not the
    /// system default.
    /// </summary>
    /// <remarks>
    /// Read from the resources themselves rather than from
    /// <c>CultureInfo.CurrentUICulture</c>, so it always matches the .resw file
    /// the user is really reading. The parity test keeps the key present in all
    /// three, and a missing one degrades to the key name, which the plugin maps
    /// to English like any other unknown tag.
    /// </remarks>
    public string Language => UiText.Get("Meta_LanguageTag");

    /// <summary>
    /// What is already known, without touching the network. Null means "not
    /// looked up yet", which is why the page renders with this first and fills
    /// the rest in afterwards.
    /// </summary>
    public TagArticle? Cached(string providerId, string tag)
    {
        var provider = plugins.TagDictionaryFor(providerId);
        if (provider is null) return null;
        try
        {
            return provider.TryGetCached(tag, Language);
        }
        catch (Exception ex)
        {
            logger.LogError("TagDictionary.Cached", ex, $"{providerId}:{tag}");
            return null;
        }
    }

    /// <summary>
    /// Looks one tag up, going to the provider when it is not cached. Returns
    /// null when the platform has no dictionary or the provider has nothing.
    /// </summary>
    public async Task<TagArticle?> FetchAsync(string providerId, string tag, CancellationToken token)
    {
        var provider = plugins.TagDictionaryFor(providerId);
        if (provider is null) return null;
        try
        {
            return await provider.FetchTagAsync(tag, Language, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A plugin that throws must not take the tag page with it; the tag
            // just stays untranslated.
            logger.LogError("TagDictionary.Fetch", ex, $"{providerId}:{tag}");
            return null;
        }
    }

    /// <summary>
    /// Looks <paramref name="tags"/> up one at a time, reporting each as it
    /// arrives.
    /// </summary>
    /// <remarks>
    /// Sequential on purpose. The provider paces its own requests, so issuing
    /// sixty at once would only queue sixty waits and lose the ordering that
    /// makes the labels appear top-down. Reporting each one instead of the
    /// finished set is what keeps a cold cache from looking like a hang: the
    /// cloud is already on screen, and the words relabel themselves as the
    /// answers come in.
    /// </remarks>
    public async Task WarmAsync(
        string providerId,
        IEnumerable<string> tags,
        Action<string, TagArticle> onResolved,
        CancellationToken token)
    {
        if (plugins.TagDictionaryFor(providerId) is null) return;

        foreach (var tag in tags)
        {
            token.ThrowIfCancellationRequested();
            var article = await FetchAsync(providerId, tag, token);
            if (article is not null) onResolved(tag, article);
        }
    }
}

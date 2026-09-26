namespace SceneGallery.PluginSdk;

/// <summary>
/// What a provider knows about one tag: how to say it in the user's language,
/// and what it means.
/// </summary>
/// <param name="Tag">The tag as asked for, unchanged.</param>
/// <param name="Translation">
/// The tag in the requested language, or null when the provider has none. A
/// provider may fall back to another language it does have (pixiv answers a
/// Chinese request with the English name when no Chinese one exists); callers
/// should treat this as "a better label than the raw tag", not as a guarantee
/// of the language asked for.
/// </param>
/// <param name="Reading">Romanised or phonetic reading, when the provider has one.</param>
/// <param name="Summary">
/// A short description of the tag. <b>In whatever language the provider's
/// encyclopedia is written in</b>, which is not necessarily the requested one —
/// pixiv's is Japanese throughout. Null or empty when no article exists.
/// </param>
/// <param name="ImageUrl">Representative image, when the provider offers one.</param>
/// <param name="ParentTags">
/// Broader tags, outermost first, without the tag itself.
/// <para>
/// This also carries the answer for an alias. Providers keep entries whose
/// article is a one-line pointer rather than a description — a romanisation, an
/// alternate spelling — and the tag it points at is the innermost parent. There
/// is deliberately no separate "canonical tag": pixiv's payload describes an
/// alias and a genuine sub-topic identically (「東方」のローマ字表記。 and
/// 「東方Project」の舞台。 differ only in prose), so a flag here would be a
/// guess dressed up as a fact. The app offers the innermost parent as a link
/// instead and lets the reader decide.
/// </para>
/// </param>
/// <param name="ChildTags">Narrower tags.</param>
/// <param name="AliasOf">
/// The tag this one is merely another spelling of, when the provider can say
/// so, and null otherwise — including when it simply cannot tell.
/// <para>
/// Distinct from <see cref="TagArticle.ParentTags"/> on purpose. A parent is a
/// broader topic and says nothing about identity: 幻想郷's parent is 東方Project
/// and they are different tags. This says the two name the same thing, so the
/// app may fold one into the other and add their artworks together. A provider
/// that is unsure must leave it null: a wrong claim here silently swallows a
/// real tag's cards.
/// </para>
/// </param>
/// <param name="ArticleUrl">Browser-openable article address, for "read more".</param>
public sealed record TagArticle(
    string Tag,
    string? Translation,
    string? Reading,
    string? Summary,
    string? ImageUrl,
    IReadOnlyList<string> ParentTags,
    IReadOnlyList<string> ChildTags,
    string? ArticleUrl,
    string? AliasOf = null)
{
    /// <summary>Whether this carries anything worth showing.</summary>
    public bool HasArticle => !string.IsNullOrWhiteSpace(Summary);
}

/// <summary>
/// A plugin that can describe and translate the tags of its own platform.
/// Optional: a provider without an encyclopedia simply does not implement it,
/// and the app then shows that platform's tags untranslated.
/// </summary>
/// <remarks>
/// The app supplies a BCP-47 language tag and never a provider-specific code.
/// Mapping "zh-Hant" onto whatever the service calls it stays inside the
/// plugin, along with the network access, the pacing and the cache.
/// </remarks>
public interface ITagDictionaryProvider : IPlugin
{
    /// <summary>Provider identifier, matching <see cref="ICardImportProvider.ProviderId"/>.</summary>
    string ProviderId { get; }

    /// <summary>
    /// Looks one tag up. Rate-limited and cached internally. Returns null when
    /// the provider has nothing at all for it, which is not an error.
    /// </summary>
    /// <param name="language">BCP-47, e.g. "zh-Hant", "zh-Hans", "en-US".</param>
    Task<TagArticle?> FetchTagAsync(string tag, string language, CancellationToken ct);

    /// <summary>
    /// Whatever is already cached, without going near the network. The app uses
    /// this to render immediately and then fills the rest in as it arrives, so
    /// a cold cache never makes the user wait.
    /// </summary>
    TagArticle? TryGetCached(string tag, string language) => null;
}

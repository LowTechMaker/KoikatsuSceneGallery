using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>One tag on one platform, with the artworks that carry it.</summary>
/// <param name="PostKeys">
/// Grouping keys, ready to match against a card's own key. Built through
/// <see cref="GalleryGrouping.GetKey"/> rather than formatted here, so the two
/// sides cannot drift apart.
/// </param>
public sealed record TagCloudEntry(
    string Name, string? TranslatedName, int Count, IReadOnlyList<string> PostKeys)
{
    public string Display => string.IsNullOrWhiteSpace(TranslatedName) ? Name : TranslatedName;

    /// <summary>
    /// Other spellings folded into this tag, if any. Kept so the merge can be
    /// shown rather than silently applied — a count that grew needs to say
    /// where the extra artworks came from.
    /// </summary>
    public IReadOnlyList<string> MergedFrom { get; init; } = [];
}

/// <summary>The tags of one platform. Platforms with no tags never get a group.</summary>
public sealed record TagCloudProviderGroup(string ProviderId, IReadOnlyList<TagCloudEntry> Tags);

/// <summary>
/// Turns the per-author sidecar documents into a per-platform tag cloud.
/// </summary>
internal static class TagCloudAggregator
{
    /// <summary>
    /// Tags shown per platform. A hard cap, not a preference: the cloud panel
    /// does not virtualize, so every tag placed is a realized element carrying
    /// its own idle animation.
    /// </summary>
    public const int MaxPerProvider = 60;

    public const double MinFontSize = 14;
    public const double MaxFontSize = 48;

    public static IReadOnlyList<TagCloudProviderGroup> Aggregate(
        IEnumerable<PostMetadataDocument> documents, int maxPerProvider = MaxPerProvider)
    {
        // The same artwork copied into two rating folders or two library roots
        // has an identical sidecar under each, and counting both would double
        // every one of its tags.
        var unique = documents
            .DistinctBy(doc => (doc.ProviderId, doc.ArtworkId), ProviderArtworkComparer.Instance);

        var byProvider = new Dictionary<string, Dictionary<string, TagAccumulator>>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in unique)
        {
            if (string.IsNullOrWhiteSpace(doc.ProviderId)) continue;
            if (!byProvider.TryGetValue(doc.ProviderId, out var tags))
                byProvider[doc.ProviderId] = tags = new(StringComparer.OrdinalIgnoreCase);

            var postKey = GalleryGrouping.GetKey(string.Empty, doc.ProviderId, doc.ArtworkId);
            foreach (var tag in doc.Tags ?? [])
            {
                var name = tag.Name?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                if (!tags.TryGetValue(name, out var accumulator))
                    tags[name] = accumulator = new(name);
                accumulator.Add(tag.TranslatedName, postKey);
            }
        }

        return [.. byProvider
            .Where(pair => pair.Value.Count > 0)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new TagCloudProviderGroup(pair.Key, [.. pair.Value.Values
                .Select(accumulator => accumulator.ToEntry())
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)
                .Take(maxPerProvider)]))];
    }

    /// <summary>
    /// Type size for a tag, on a square-root curve. Linear scaling lets one
    /// dominant tag flatten every other tag to the minimum.
    /// </summary>
    public static double FontSize(
        int count, int minCount, int maxCount, double minPt = MinFontSize, double maxPt = MaxFontSize)
    {
        if (maxCount <= minCount) return (minPt + maxPt) / 2;
        var t = (Math.Sqrt(Math.Max(count, minCount)) - Math.Sqrt(minCount))
                / (Math.Sqrt(maxCount) - Math.Sqrt(minCount));
        return Math.Clamp(minPt + (maxPt - minPt) * t, minPt, maxPt);
    }

    private sealed class TagAccumulator(string name)
    {
        private readonly Dictionary<string, int> _translations = new(StringComparer.Ordinal);
        private readonly List<string> _postKeys = [];

        public void Add(string? translatedName, string postKey)
        {
            _postKeys.Add(postKey);
            if (string.IsNullOrWhiteSpace(translatedName)) return;
            _translations.TryGetValue(translatedName, out var seen);
            _translations[translatedName] = seen + 1;
        }

        public TagCloudEntry ToEntry()
        {
            // Documents disagree about translations; the commonest wins, with
            // the name itself breaking a tie so the result is deterministic.
            var translated = _translations.Count == 0
                ? null
                : _translations
                    .OrderByDescending(pair => pair.Value)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .First().Key;
            return new(name, translated, _postKeys.Count, _postKeys);
        }
    }

    private sealed class ProviderArtworkComparer : IEqualityComparer<(string ProviderId, string ArtworkId)>
    {
        public static readonly ProviderArtworkComparer Instance = new();

        public bool Equals((string ProviderId, string ArtworkId) a, (string ProviderId, string ArtworkId) b)
            => string.Equals(a.ProviderId, b.ProviderId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(a.ArtworkId, b.ArtworkId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string ProviderId, string ArtworkId) value)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ProviderId ?? ""),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.ArtworkId ?? ""));
    }
}

using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class CardGroupingService
{
    private const int DefaultPHashThreshold = 22;
    private const float DefaultHistogramCorrelation = 0.45f;
    private const int BorderlinePHashLow = 18;
    private const int BorderlinePHashHigh = 28;
    private const float LenientHistogramCorrelation = 0.2f;

    public static List<List<ImportItem>> GroupByVisualSimilarity(
        IReadOnlyList<ImportItem> items,
        int pHashThreshold = DefaultPHashThreshold,
        float histogramThreshold = DefaultHistogramCorrelation)
    {
        int n = items.Count;
        if (n == 0) return [];

        var parent = new int[n];
        var rank = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (ShouldUnion(items[i], items[j], pHashThreshold, histogramThreshold))
                    Union(parent, rank, i, j);
            }
        }

        var groups = new Dictionary<int, List<ImportItem>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(parent, i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = [];
                groups[root] = list;
            }
            list.Add(items[i]);
        }

        var result = groups.Values.ToList();
        result.Sort((a, b) =>
        {
            int cmp = b.Count.CompareTo(a.Count);
            return cmp != 0 ? cmp : string.Compare(a[0].FileName, b[0].FileName, StringComparison.OrdinalIgnoreCase);
        });

        return result;
    }

    public static bool AreVisuallyRelated(ImportItem a, ImportItem b,
        int pHashThreshold = DefaultPHashThreshold,
        float histogramThreshold = DefaultHistogramCorrelation) =>
        ShouldUnion(a, b, pHashThreshold, histogramThreshold);

    /// <summary>
    /// Determines if the majority of items in a group are visually related,
    /// which means they should be placed in an artwork subfolder.
    /// Returns null if fingerprints are unavailable (caller should fallback).
    /// </summary>
    public static bool? ShouldGroupAsArtwork(IReadOnlyList<ImportItem> items)
    {
        if (items.Count < 2) return false;

        int totalPairs = 0;
        int relatedPairs = 0;
        bool anyFingerprint = false;

        for (int i = 0; i < items.Count; i++)
        {
            for (int j = i + 1; j < items.Count; j++)
            {
                if (items[i].PHash is null && items[j].PHash is null)
                    continue;

                anyFingerprint = true;
                totalPairs++;
                if (AreVisuallyRelated(items[i], items[j]))
                    relatedPairs++;
            }
        }

        if (!anyFingerprint) return null;
        if (totalPairs == 0) return null;

        return relatedPairs >= Math.Max(1, (int)Math.Ceiling(totalPairs * 0.3));
    }

    private static bool ShouldUnion(ImportItem a, ImportItem b,
        int pHashThreshold, float histogramThreshold)
    {
        if (a.PHash is null || b.PHash is null) return false;

        bool sameFolder = string.Equals(a.SourceFolder, b.SourceFolder, StringComparison.OrdinalIgnoreCase);
        int distance = ImageFingerprintService.HammingDistance(a.PHash.Value, b.PHash.Value);

        int effectiveThreshold = sameFolder ? BorderlinePHashHigh : pHashThreshold;
        float effectiveCorrelation = sameFolder ? LenientHistogramCorrelation : histogramThreshold;

        if (distance > effectiveThreshold) return false;

        if (a.ColorHistogram is null || b.ColorHistogram is null)
            return distance <= effectiveThreshold;

        float corr = ImageFingerprintService.HistogramCorrelation(a.ColorHistogram, b.ColorHistogram);
        return corr >= effectiveCorrelation;
    }

    private static int Find(int[] parent, int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return x;
    }

    private static void Union(int[] parent, int[] rank, int x, int y)
    {
        int rx = Find(parent, x), ry = Find(parent, y);
        if (rx == ry) return;
        if (rank[rx] < rank[ry]) (rx, ry) = (ry, rx);
        parent[ry] = rx;
        if (rank[rx] == rank[ry]) rank[rx]++;
    }
}

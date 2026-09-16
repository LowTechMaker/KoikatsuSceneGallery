namespace KoikatsuSceneGallery.Helpers;

public enum ImportReviewRowKind { Section, Group, ListItem, GridRow }
public enum ImportReviewSection { Unidentified, Unavailable, Identified }

public sealed record ImportReviewLayoutRow<T>(
    string Key, ImportReviewRowKind Kind, ImportReviewSection Section, IReadOnlyList<T> Items);

/// <summary>One scrolling sequence: attention rows first, then bounded rows of image tiles.</summary>
public static class ImportReviewLayout
{
    public static int ColumnsForWidth(double width)
        => Math.Clamp((int)(Math.Max(0, width) / 220), 1, 6);

    public static IReadOnlyList<ImportReviewLayoutRow<T>> Build<T>(IEnumerable<T> items,
        Func<T, ImportReviewSection> sectionFor, Func<T, string> groupKey, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        var snapshot = items.ToArray();
        var result = new List<ImportReviewLayoutRow<T>>();
        foreach (var sectionKind in Enum.GetValues<ImportReviewSection>())
        {
            var identified = sectionKind == ImportReviewSection.Identified;
            var section = snapshot.Where(item => sectionFor(item) == sectionKind).ToArray();
            var prefix = sectionKind + ":";
            result.Add(new(prefix, ImportReviewRowKind.Section, sectionKind, section));
            foreach (var group in section.GroupBy(groupKey, StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToArray();
                var key = prefix + group.Key;
                result.Add(new(key + ":header", ImportReviewRowKind.Group, sectionKind, members));
                var chunks = members.Chunk(identified ? columns : 1);
                var index = 0;
                foreach (var chunk in chunks)
                    result.Add(new(key + ":row:" + index++, identified ? ImportReviewRowKind.GridRow
                        : ImportReviewRowKind.ListItem, sectionKind, chunk));
            }
        }
        return result;
    }
}

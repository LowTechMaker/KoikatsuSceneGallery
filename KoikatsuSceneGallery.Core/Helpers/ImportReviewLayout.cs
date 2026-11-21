namespace KoikatsuSceneGallery.Helpers;

public enum ImportReviewRowKind { Section, Group, ListItem, ModItem }

/// <summary>Sections in the order the list shows them: what needs the user first, what is left alone last.</summary>
public enum ImportReviewSection { Unidentified, Unavailable, Identified, Mods, Skipped }

public sealed record ImportReviewLayoutRow<T>(
    string Key, ImportReviewRowKind Kind, ImportReviewSection Section, IReadOnlyList<T> Items);

/// <summary>
/// One scrolling list: a header per section that has anything in it, a header per post or
/// folder inside, then one row per card. A collapsed section keeps only its header.
/// </summary>
/// <remarks>
/// Mods are not cards and are laid out by the workspace itself, between the identified
/// cards and the skipped ones; <see cref="ImportReviewSection.Mods"/> never comes out of here.
/// </remarks>
public static class ImportReviewLayout
{
    public static IReadOnlyList<ImportReviewLayoutRow<T>> Build<T>(IEnumerable<T> items,
        Func<T, ImportReviewSection> sectionFor, Func<T, string> groupKey,
        IReadOnlySet<ImportReviewSection>? collapsed = null)
    {
        var snapshot = items.ToArray();
        var result = new List<ImportReviewLayoutRow<T>>();
        foreach (var sectionKind in Enum.GetValues<ImportReviewSection>())
        {
            if (sectionKind == ImportReviewSection.Mods) continue;
            var section = snapshot.Where(item => sectionFor(item) == sectionKind).ToArray();
            if (section.Length == 0) continue;

            var prefix = sectionKind + ":";
            result.Add(new(prefix, ImportReviewRowKind.Section, sectionKind, section));
            if (collapsed?.Contains(sectionKind) == true) continue;

            foreach (var group in section.GroupBy(groupKey, StringComparer.OrdinalIgnoreCase))
            {
                var members = group.ToArray();
                var key = prefix + group.Key;
                result.Add(new(key + ":header", ImportReviewRowKind.Group, sectionKind, members));
                var index = 0;
                foreach (var member in members)
                    result.Add(new(key + ":row:" + index++, ImportReviewRowKind.ListItem, sectionKind, [member]));
            }
        }
        return result;
    }
}

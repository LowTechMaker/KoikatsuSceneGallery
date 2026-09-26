namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Folds tags that are only other spellings of another tag into it.
/// </summary>
/// <remarks>
/// Which tags those are is the platform's answer, not this app's — the fold is
/// given a lookup and never inspects a tag itself. What it owns is the
/// arithmetic, and the one thing that is easy to get wrong there: an artwork
/// carrying both コイカツ and コイカツ! must be counted once, so the merged
/// count is the size of the union of the artworks, never the sum of the counts.
/// </remarks>
internal static class TagAliasFold
{
    /// <summary>
    /// Merges each alias into its target within the same platform.
    /// </summary>
    /// <param name="groups">The cloud, per platform.</param>
    /// <param name="aliasOf">
    /// The tag a given tag is another name for, or null. Consulted only for
    /// tags in <paramref name="groups"/>.
    /// </param>
    /// <remarks>
    /// A tag is folded only when its target is present in the same group.
    /// Folding into something not shown would make the alias vanish instead of
    /// merging it, which is worse than leaving it alone.
    ///
    /// Chains are followed (koikatsu! → コイカツ! → …). A cycle is refused
    /// outright rather than broken at an arbitrary point: with A an alias of B
    /// and B an alias of A, folding each into the other leaves nothing at all,
    /// so every tag in a cycle keeps its own entry.
    /// </remarks>
    public static IReadOnlyList<TagCloudProviderGroup> Fold(
        IReadOnlyList<TagCloudProviderGroup> groups, Func<string, string?> aliasOf)
    {
        var folded = new List<TagCloudProviderGroup>(groups.Count);
        foreach (var group in groups)
            folded.Add(FoldGroup(group, aliasOf));
        return folded;
    }

    private static TagCloudProviderGroup FoldGroup(TagCloudProviderGroup group, Func<string, string?> aliasOf)
    {
        var byName = group.Tags.ToDictionary(tag => tag.Name, StringComparer.Ordinal);
        var target = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var tag in group.Tags)
        {
            var resolved = Resolve(tag.Name, byName, aliasOf);
            if (resolved is not null) target[tag.Name] = resolved;
        }

        if (target.Count == 0) return group;

        var merged = new List<TagCloudEntry>(group.Tags.Count);
        foreach (var tag in group.Tags)
        {
            if (target.ContainsKey(tag.Name)) continue;

            var absorbed = group.Tags
                .Where(other => target.TryGetValue(other.Name, out var into)
                                && string.Equals(into, tag.Name, StringComparison.Ordinal))
                .ToArray();

            if (absorbed.Length == 0)
            {
                merged.Add(tag);
                continue;
            }

            // Union, not sum: the same artwork is very often tagged with both
            // spellings, and adding the counts would inflate the merged tag and
            // with it the size it is drawn at.
            var keys = new HashSet<string>(tag.PostKeys, StringComparer.Ordinal);
            foreach (var other in absorbed) keys.UnionWith(other.PostKeys);

            merged.Add(tag with
            {
                Count = keys.Count,
                PostKeys = [.. keys],
                MergedFrom = [.. absorbed.Select(other => other.Name).OrderBy(name => name, StringComparer.Ordinal)],
            });
        }

        return group with
        {
            Tags = [.. merged
                .OrderByDescending(entry => entry.Count)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal)],
        };
    }

    /// <summary>
    /// The tag <paramref name="name"/> should end up in, or null when it stays
    /// where it is.
    /// </summary>
    private static string? Resolve(
        string name, Dictionary<string, TagCloudEntry> byName, Func<string, string?> aliasOf)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { name };
        var current = name;

        while (true)
        {
            var next = aliasOf(current);
            if (string.IsNullOrEmpty(next)) break;
            if (!byName.ContainsKey(next)) break;
            // Coming back to a tag already on this path means the claims form
            // a loop. Stopping here and folding anyway would make both ends
            // fold into each other and leave the group empty.
            if (!seen.Add(next)) return null;
            current = next;
        }

        return string.Equals(current, name, StringComparison.Ordinal) ? null : current;
    }
}

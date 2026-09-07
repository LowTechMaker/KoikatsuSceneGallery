namespace KoikatsuSceneGallery.Services;

public static class ImportSourceSelection
{
    public static string? Resolve(string? selectedSource, IReadOnlyCollection<string> installedSources, IEnumerable<string> matchingSources)
    {
        if (!string.IsNullOrWhiteSpace(selectedSource))
            return installedSources.FirstOrDefault(s => string.Equals(s, selectedSource, StringComparison.OrdinalIgnoreCase));
        var matches = matchingSources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length == 1 ? matches[0]
            : matches.Length == 0 && installedSources.Count == 1 ? installedSources.First() : null;
    }
}

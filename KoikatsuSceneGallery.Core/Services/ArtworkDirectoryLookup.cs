namespace KoikatsuSceneGallery.Services;

internal static class ArtworkDirectoryLookup
{
    // Candidates retain caller ordering/filtering. Null means no match. Parser and
    // enumeration exceptions (including cancellation) propagate to the caller.
    internal static string? FindFirst(
        IEnumerable<string> candidates, string providerId, string artworkId,
        Func<string, (string ProviderId, string Id)?> parseFolderName)
    {
        foreach (var path in candidates)
        {
            var parsed = parseFolderName(Path.GetFileName(path));
            if (parsed is { } identity
                && identity.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)
                && identity.Id.Equals(artworkId, StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }
}

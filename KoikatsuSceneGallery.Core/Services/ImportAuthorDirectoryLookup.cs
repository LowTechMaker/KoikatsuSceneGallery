namespace KoikatsuSceneGallery.Services;

// Searches a completed index only; ordering and dictionary comparers belong to callers.
internal static class ImportAuthorDirectoryLookup
{
    internal static string? FindExact(
        IEnumerable<string> roots,
        string providerFolder,
        string gameFolder,
        string ratingFolder,
        string authorId,
        Dictionary<string, Dictionary<string, string>> index)
    {
        foreach (var root in roots)
        {
            var key = ImportLibraryIndexer.BuildScopeKey(root, providerFolder, gameFolder, ratingFolder);
            if (index.TryGetValue(key, out var authors) && authors.TryGetValue(authorId, out var directory))
                return directory;
        }
        return null;
    }

    internal static (string? Directory, string? ProviderFolder) FindFallback(
        IEnumerable<string> roots,
        IEnumerable<(string Folder, bool UsesRatingFolders)> providerScopes,
        IEnumerable<string> gameFolders,
        string gameFolder,
        string ratingFolder,
        string authorId,
        Dictionary<string, Dictionary<string, string>> index)
    {
        foreach (var root in roots)
        {
            foreach (var scope in providerScopes)
            {
                var rating = scope.UsesRatingFolders ? ratingFolder : "";
                var exactKey = ImportLibraryIndexer.BuildScopeKey(root, scope.Folder, gameFolder, rating);
                if (index.TryGetValue(exactKey, out var authors) && authors.TryGetValue(authorId, out var directory))
                    return (directory, null);

                foreach (var game in gameFolders)
                {
                    if (game == gameFolder) continue;
                    var alternativeKey = ImportLibraryIndexer.BuildScopeKey(root, scope.Folder, game, rating);
                    if (index.TryGetValue(alternativeKey, out var alternatives) && alternatives.ContainsKey(authorId))
                        return (null, scope.Folder);
                }
            }
        }
        return (null, null);
    }
}

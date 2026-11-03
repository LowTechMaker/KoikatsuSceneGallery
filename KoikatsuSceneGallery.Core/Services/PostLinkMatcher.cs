using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// A file a post's links list: its name, its size in bytes when the service says so, and an
/// address that opens it when the service has one. <paramref name="Folder"/> is the folder it sits
/// in inside the share (null at the root) and <paramref name="FolderUrl"/> opens that folder.
/// </summary>
public sealed record ListedFile(
    string Name, long? Size, string? Url = null, string? Folder = null, string? FolderUrl = null);

/// <summary>
/// A folder of the share the user has not started: none of its files was dropped in or is in
/// the library. Authors share a whole series in one folder, split by episode, and a post for one
/// episode links to all of it, so these are episodes not being worked on, not files that are missing.
/// </summary>
/// <param name="Folder">The folder path, null for the root of the share.</param>
public sealed record UnstartedFolder(string? Folder, string? FolderUrl, int Files);

/// <summary>How the files a post's links list line up with the cards the user dropped in.</summary>
/// <param name="ListedCount">Distinct file names the links list.</param>
/// <param name="Matched">Dropped card names the post lists, as the user's files are spelled.</param>
/// <param name="Missing">Listed names with no dropped card and none in the library, as the post spells them.</param>
/// <param name="Unlisted">Dropped card names the post does not list.</param>
public sealed record PostLinkMatch(
    int ListedCount,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unlisted)
{
    /// <summary>
    /// Listed names with no dropped card, which the library already holds an identical
    /// file for. Not a loss: reported apart from <see cref="Missing"/>.
    /// </summary>
    public IReadOnlyList<string> InLibrary { get; init; } = [];

    /// <summary><see cref="Missing"/> with each file's size and address, for showing a link.</summary>
    public IReadOnlyList<ListedFile> MissingFiles { get; init; } = [];

    /// <summary><see cref="InLibrary"/> with each file's size and address.</summary>
    public IReadOnlyList<ListedFile> InLibraryFiles { get; init; } = [];

    /// <summary>
    /// Folders of the share nothing has been taken from. Their files are in neither
    /// <see cref="Missing"/> nor <see cref="InLibrary"/>.
    /// </summary>
    public IReadOnlyList<UnstartedFolder> UnstartedFolders { get; init; } = [];
}

/// <summary>Compares file names only. Pure: no I/O.</summary>
public static class PostLinkMatcher
{
    // Case is ignored, and so is the " (1)" Windows adds when a download would overwrite
    // a file: the author's "card.png" and the user's "card (1).png" are the same card.
    private static string Key(string name) => CopySuffix.Normalize(name).ToUpperInvariant();

    /// <param name="listedNames">File names from the post's links; repeats count once.</param>
    /// <param name="cardNames">File names (without folders) of the cards in the import list.</param>
    public static PostLinkMatch Match(IEnumerable<string> listedNames, IEnumerable<string> cardNames)
        => MatchFiles(listedNames.Select(name => new ListedFile(name, null)), cardNames, libraryHas: null);

    /// <param name="listed">Files from the post's links; repeats count once.</param>
    /// <param name="cardNames">File names (without folders) of the cards in the import list.</param>
    /// <param name="libraryHas">
    /// Whether the library already holds a file of that name and size (size null: any size).
    /// Asked only for listed files with no dropped card. Null means nothing is known.
    /// </param>
    public static PostLinkMatch MatchFiles(
        IEnumerable<ListedFile> listed,
        IEnumerable<string> cardNames,
        Func<string, long?, bool>? libraryHas)
    {
        var files = new Dictionary<string, ListedFile>(StringComparer.Ordinal);
        foreach (var file in listed)
            if (!string.IsNullOrWhiteSpace(file.Name)) files.TryAdd(Key(file.Name), file);

        var matched = new List<string>();
        var unlisted = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cardNames)
        {
            var key = Key(card);
            if (files.ContainsKey(key)) { matched.Add(card); seen.Add(key); }
            else unlisted.Add(card);
        }

        var notDropped = new List<ListedFile>();
        var inLibrary = new List<ListedFile>();
        foreach (var (key, file) in files)
        {
            if (seen.Contains(key)) continue;
            if (libraryHas?.Invoke(file.Name, file.Size) == true) inLibrary.Add(file);
            else notDropped.Add(file);
        }

        // A folder is started once anything in it was dropped in or is already in the library.
        // What is left in a folder nothing was taken from is an episode not being worked on.
        var started = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (key, file) in files)
            if (seen.Contains(key)) started.Add(file.Folder ?? string.Empty);
        foreach (var file in inLibrary) started.Add(file.Folder ?? string.Empty);

        // Episodes only exist when the share has more than one folder; a flat share is one work.
        var hasSeveralFolders = files.Values.Select(file => file.Folder ?? string.Empty).Distinct(StringComparer.Ordinal).Skip(1).Any();
        bool IsStarted(ListedFile file) => !hasSeveralFolders || started.Contains(file.Folder ?? string.Empty);

        var missing = notDropped.Where(IsStarted).ToList();
        var unstarted = notDropped
            .Where(file => !IsStarted(file))
            .GroupBy(file => file.Folder ?? string.Empty, StringComparer.Ordinal)
            .Select(group => new UnstartedFolder(
                group.Key.Length == 0 ? null : group.Key,
                group.Select(file => file.FolderUrl).FirstOrDefault(url => url is not null),
                group.Count()))
            .ToList();

        return new PostLinkMatch(files.Count, matched, missing.Select(f => f.Name).ToList(), unlisted)
        {
            InLibrary = inLibrary.Select(f => f.Name).ToList(),
            MissingFiles = missing,
            InLibraryFiles = inLibrary,
            UnstartedFolders = unstarted,
        };
    }
}

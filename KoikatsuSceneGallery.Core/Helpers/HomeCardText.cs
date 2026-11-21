namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// What a home page tile says about a card: its title and a few tags, instead
/// of a file name like <c>136615689_p8.png</c>.
/// </summary>
internal static class HomeCardText
{
    /// <summary>
    /// The post's title when its sidecar has one; else the title in the card's
    /// artwork folder name (<c>Title (id)</c>); else the file name without its
    /// extension.
    /// </summary>
    /// <param name="folderArtworkId">The artwork id the card's folder name parses to, or null.</param>
    /// <param name="authorId">The card's author id: an author folder parses like an artwork folder.</param>
    public static string Title(string? postTitle, string fileName, string? folderName, string? folderArtworkId, string? authorId)
    {
        if (!string.IsNullOrWhiteSpace(postTitle)) return postTitle.Trim();
        if (folderName is not null && folderArtworkId is not null
            && !string.Equals(folderArtworkId, authorId, StringComparison.OrdinalIgnoreCase)
            && FromFolder(folderName, folderArtworkId) is { } folderTitle)
            return folderTitle;
        return Path.GetFileNameWithoutExtension(fileName);
    }

    // As AuthorPostService.ExtractTitleFromFolderName: the text before "(id)".
    private static string? FromFolder(string folderName, string artworkId)
    {
        var index = folderName.IndexOf($"({artworkId})", StringComparison.Ordinal);
        if (index <= 0) return null;
        var title = folderName[..index].Trim();
        return title.Length == 0 ? null : title;
    }

    /// <summary>Up to <paramref name="max"/> tags, the translation when there is one, without blanks or repeats.</summary>
    public static IReadOnlyList<string> Tags(IEnumerable<(string Name, string? Translated)> tags, int max)
        => [.. tags
            .Select(tag => string.IsNullOrWhiteSpace(tag.Translated) ? tag.Name : tag.Translated)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(max)];
}

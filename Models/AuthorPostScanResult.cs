namespace KoikatsuSceneGallery.Models;

/// <summary>A local image which has not yet been associated with an artwork post.</summary>
public sealed record UnassignedAuthorImage(string FilePath, string AuthorDirectory)
{
    public string FileName => Path.GetFileName(FilePath);

    public Uri ImageUri => new(FilePath.Replace("#", "%23"));
}

/// <summary>Posts and local files that were not attributable to any post during a scan.</summary>
public sealed record AuthorPostScanResult(
    IReadOnlyList<AuthorPost> Posts,
    IReadOnlyList<UnassignedAuthorImage> UnassignedImages);

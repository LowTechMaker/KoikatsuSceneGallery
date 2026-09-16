using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Projects an already filtered and sorted image list without changing its order.</summary>
public static class GalleryGrouping
{
    public const int Threshold = 5;

    /// <summary>Import sink for files whose artwork could not be identified; its members are unrelated.</summary>
    public const string UnrecognizedFolderName = "!unrecognized";

    public static string ResolveKey(string filePath, string? providerId, string? folderPostId,
        string? filePostId, string? authorId)
    {
        // Author and artwork folders can share the same "name (id)" syntax.
        var postId = folderPostId is not null && folderPostId != authorId ? folderPostId : filePostId;
        var key = GetKey(filePath, providerId, postId);

        // A card sitting directly in its author's own folder has no identity
        // beyond the author, so grouping by that folder would collapse the
        // whole author into one tile. Remote cards avoid this because the ones
        // without an artwork id land in the unrecognized sink, which is
        // degraded below; local cards are exempt from that sink by design and
        // so would otherwise all share the source folder.
        return key.StartsWith("folder:", StringComparison.Ordinal)
               && IsOwnAuthorFolder(filePath, authorId)
            ? $"file:{filePath}"
            : key;
    }

    private static bool IsOwnAuthorFolder(string filePath, string? authorId)
    {
        if (string.IsNullOrEmpty(authorId))
            return false;

        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath));
        return LocalSourceIdentity.TryParseFolderId(folderName, out var id, out _)
               && string.Equals(id, authorId, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetKey(string filePath, string? providerId = null, string? postId = null)
    {
        if (!string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(postId))
            return $"post:{providerId}:{postId}";
        // A card from a local source has no remote post, so a digit run left in
        // a privately shared file name must not be read as one.
        if (!LocalSourceIdentity.IsLocal(providerId))
        {
            var link = FilenameLinkParser.Parse(filePath);
            if (link.PixivArtworkId is { } pixiv)
                return $"post:pixiv:{pixiv}";
            if (link.BepisDbId is { } bepis)
                return $"post:bepisdb:{bepis}";
        }
        var folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder)) return $"file:{filePath}";
        // The unrecognized sink holds unrelated files, so folder identity must not merge them.
        return string.Equals(Path.GetFileName(folder), UnrecognizedFolderName, StringComparison.OrdinalIgnoreCase)
            ? $"file:{filePath}" : $"folder:{folder}";
    }

    public static IReadOnlyList<GalleryGroup<T>> Create<T>(IReadOnlyList<T> images, Func<T, string> keySelector)
    {
        var buckets = new Dictionary<string, List<T>>(StringComparer.OrdinalIgnoreCase);
        var keys = new string[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            var key = keys[i] = keySelector(images[i]);
            if (!buckets.TryGetValue(key, out var members)) buckets[key] = members = [];
            members.Add(images[i]);
        }
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<GalleryGroup<T>>();
        for (var i = 0; i < images.Count; i++)
        {
            var key = keys[i];
            var members = buckets[key];
            if (members.Count <= Threshold)
                result.Add(new(key, [images[i]]));
            else if (emitted.Add(key))
                result.Add(new(key, members));
        }
        return result;
    }
}

public sealed record GalleryGroup<T>(string Key, IReadOnlyList<T> Members);

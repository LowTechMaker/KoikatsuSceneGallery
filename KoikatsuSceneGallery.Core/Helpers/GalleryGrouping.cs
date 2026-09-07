namespace KoikatsuSceneGallery.Helpers;

/// <summary>Projects an already filtered and sorted image list without changing its order.</summary>
public static class GalleryGrouping
{
    public const int Threshold = 5;

    public static string ResolveKey(string filePath, string? providerId, string? folderPostId,
        string? filePostId, string? authorId)
    {
        // Author and artwork folders can share the same "name (id)" syntax.
        var postId = folderPostId is not null && folderPostId != authorId ? folderPostId : filePostId;
        return GetKey(filePath, providerId, postId);
    }

    public static string GetKey(string filePath, string? providerId = null, string? postId = null)
    {
        if (!string.IsNullOrWhiteSpace(providerId) && !string.IsNullOrWhiteSpace(postId))
            return $"post:{providerId}:{postId}";
        var link = FilenameLinkParser.Parse(filePath);
        if (link.PixivArtworkId is { } pixiv)
            return $"post:pixiv:{pixiv}";
        if (link.BepisDbId is { } bepis)
            return $"post:bepisdb:{bepis}";
        var folder = Path.GetDirectoryName(filePath);
        return string.IsNullOrEmpty(folder) ? $"file:{filePath}" : $"folder:{folder}";
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

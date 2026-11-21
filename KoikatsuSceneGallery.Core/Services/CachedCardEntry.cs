namespace KoikatsuSceneGallery.Services;

/// <summary>
/// One scene card as <c>scene_cards.json</c> stores it. In Core (moved from the
/// app's <c>SceneCardCacheService</c>, same namespace and JSON shape) so the
/// rules that read it can be tested.
/// </summary>
/// <param name="DateModifiedTicks">The file's last write time (local), as <c>FileInfo.LastWriteTime.Ticks</c>.</param>
/// <param name="DateCreatedTicks">
/// The file's creation time, which is what "date added" sorts by. Last and
/// optional so cache files written before it existed still load: the missing
/// field reads as 0 (no date) and the next scan fills it from the file system.
/// </param>
public record CachedCardEntry(
    string FilePath,
    long FileSize,
    long DateModifiedTicks,
    int Width,
    int Height,
    string? ThumbnailPath,
    long DateCreatedTicks = 0);

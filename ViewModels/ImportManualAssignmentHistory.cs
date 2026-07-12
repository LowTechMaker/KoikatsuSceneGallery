using KoikatsuSceneGallery.Models;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.ViewModels;

internal enum ManualAssignmentSource
{
    Unknown,
    FetchFailed,
}

internal sealed record ManualImportItemState(
    ImportItem Item,
    ArtworkId? ArtworkId,
    ContentRating Rating,
    string? AuthorName,
    string? AuthorId,
    string? AuthorProviderId,
    string? Title,
    IReadOnlyList<ArtworkTag>? Tags,
    ImportItemStatus Status,
    string? ErrorMessage,
    string? ManualAuthorId,
    string? ManualArtworkId,
    string? DestinationPath);

internal sealed record ManualAssignmentUndo(
    ManualAssignmentSource Source,
    IReadOnlyList<ManualImportItemState> Items);

internal sealed class ImportManualAssignmentHistory
{
    private readonly Dictionary<ImportItem, ManualImportItemState> _baselines = [];
    private ManualAssignmentUndo? _lastAssignment;

    public void RememberBaseline(ImportItem item)
        => _baselines.TryAdd(item, CreateState(item));

    public void Capture(ManualAssignmentSource source, IReadOnlyList<ImportItem> items)
        => _lastAssignment = new ManualAssignmentUndo(
            source,
            items.Select(item => _baselines.GetValueOrDefault(item, CreateState(item))).ToList());

    public ManualAssignmentUndo? TakeUndo()
    {
        var undo = _lastAssignment;
        _lastAssignment = null;
        return undo;
    }

    public void Clear()
    {
        _baselines.Clear();
        _lastAssignment = null;
    }

    public static void Restore(ManualImportItemState state)
    {
        state.Item.ArtworkId = state.ArtworkId;
        state.Item.Rating = state.Rating;
        state.Item.AuthorName = state.AuthorName;
        state.Item.AuthorId = state.AuthorId;
        state.Item.AuthorProviderId = state.AuthorProviderId;
        state.Item.Title = state.Title;
        state.Item.Tags = state.Tags;
        state.Item.Status = state.Status;
        state.Item.ErrorMessage = state.ErrorMessage;
        state.Item.ManualAuthorId = state.ManualAuthorId;
        state.Item.ManualArtworkId = state.ManualArtworkId;
        state.Item.DestinationPath = state.DestinationPath;
    }

    private static ManualImportItemState CreateState(ImportItem item)
        => new(
            item,
            item.ArtworkId,
            item.Rating,
            item.AuthorName,
            item.AuthorId,
            item.AuthorProviderId,
            item.Title,
            item.Tags,
            item.Status,
            item.ErrorMessage,
            item.ManualAuthorId,
            item.ManualArtworkId,
            item.DestinationPath);
}

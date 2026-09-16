using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public class CharacterCardService : CardScanService<CharacterCard>
{
    private readonly CharacterAnnotationStore? _annotations;

    /// <summary>
    /// The store is optional so existing callers keep working; without it the
    /// cards simply carry no annotations.
    /// </summary>
    internal CharacterCardService(CharacterAnnotationStore? annotations = null)
        => _annotations = annotations;

    protected override IEnumerable<FileInfo> EnumerateCardFiles(string folder) =>
        new DirectoryInfo(folder).EnumerateFiles("*.png", SearchOption.AllDirectories);

    protected override void ConfigureWatcher(FileSystemWatcher watcher) =>
        watcher.Filter = "*.png";

    protected override CharacterCard? TryCreateCard(FileInfo info)
    {
        try
        {
            if (!info.Exists) return null;

            var (width, height) = PngHelper.ReadDimensions(info.FullName);
            var card = new CharacterCard
            {
                FilePath = info.FullName,
                FileSize = info.Length,
                DateModified = info.LastWriteTime,
                DateCreated = info.CreationTime,
                Width = width,
                Height = height
            };

            // Applied here because this is the one place every card is built —
            // the parallel scan and the file watcher both come through it — and
            // it already runs off the UI thread. The store reads at most once
            // per directory, so this costs a dictionary lookup per card.
            if (_annotations?.GetForCard(info.FullName) is { } annotation)
            {
                card.VersionKind = annotation.ParsedKind;
                card.IsSuperseded = annotation.IsSuperseded;
                card.VersionNote = annotation.Note;
                card.CharacterGroupKey = annotation.GroupKey;
            }

            return card;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

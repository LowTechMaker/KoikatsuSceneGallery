namespace KoikatsuSceneGallery.Models;

/// <summary>A gallery-only presentation; the underlying library remains a flat list of cards.</summary>
public sealed record SceneGalleryItem(string Key, IReadOnlyList<SceneCard> Members)
{
    public SceneCard Card => Members[0];
    public bool IsGroup => Members.Count > 1;
    public int Count => Members.Count;
    public string Title
    {
        get
        {
            if (!IsGroup) return Card.FileName;
            var folder = Path.GetFileName(Path.GetDirectoryName(Card.FilePath)) ?? Card.FileName;
            if (!Key.StartsWith("post:", StringComparison.Ordinal)) return folder;
            var identity = Key.Split(':', 3);
            return folder.Contains(identity[2], StringComparison.Ordinal) ? folder : $"{identity[1]} · {identity[2]}";
        }
    }

    public override string ToString() => IsGroup ? $"{Title} · {Count}" : Title;
}

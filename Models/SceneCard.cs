using CommunityToolkit.Mvvm.ComponentModel;

namespace KoikatsuSceneGallery.Models;

public partial class SceneCard : ObservableObject
{
    public required string FilePath { get; init; }
    public string FileName => System.IO.Path.GetFileName(FilePath);
    public long FileSize { get; init; }
    public DateTime DateModified { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Resolution => $"{Width}x{Height}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThumbnailUri))]
    [NotifyPropertyChangedFor(nameof(HasThumbnail))]
    public partial string? ThumbnailPath { get; set; }

    public Uri FileUri => new(FilePath);
    public Uri? ThumbnailUri => ThumbnailPath != null ? new(ThumbnailPath) : null;
    public bool HasThumbnail => ThumbnailPath != null;
}

public record ResolutionOption(int Width, int Height)
{
    public override string ToString() => $"{Width}x{Height}";

    public static ResolutionOption? TryParse(string s)
    {
        var parts = s.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
            return new ResolutionOption(w, h);
        return null;
    }
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace KoikatsuSceneGallery.Models;

public partial class SceneCard : CardBase, IAuthorOwner
{
    [ObservableProperty]
    public partial bool MetadataLoaded { get; set; }

    [ObservableProperty]
    public partial GameVersion Game { get; set; } = GameVersion.Unknown;

    [ObservableProperty]
    public partial bool IsR18Content { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAuthor))]
    public partial AuthorDisplay? Author { get; set; }

    public bool HasAuthor => Author != null;
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

namespace KoikatsuSceneGallery.Models;

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

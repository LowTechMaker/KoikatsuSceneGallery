namespace KoikatsuSceneGallery.Models;

public sealed record CoordinateMetadata(
    string? CoordinateName)
{
    public static readonly CoordinateMetadata Unknown = new CoordinateMetadata(CoordinateName: null);
}

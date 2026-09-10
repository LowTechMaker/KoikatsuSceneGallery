namespace KoikatsuSceneGallery.Models;

public sealed record CoordinateMetadata(
    string? CoordinateName)
{
    public CardMetadataSummary? Details { get; init; }
    public static readonly CoordinateMetadata Unknown = new CoordinateMetadata(CoordinateName: null);
}

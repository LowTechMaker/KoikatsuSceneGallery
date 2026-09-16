using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class CoordinateCardParser
{
    public static CoordinateMetadata? TryParse(string filePath) => Read(filePath, false);
    public static CoordinateMetadata? TryParseWithDetails(string filePath) => Read(filePath, true);

    private static CoordinateMetadata? Read(string filePath, bool includeDetails)
    {
        var summary = CardMetadataReader.TryRead(filePath, includeExtendedData: false)?.Summary;
        if (summary?.CardType != "KoikatuClothes") return null;
        return new CoordinateMetadata(string.IsNullOrEmpty(summary.Name) ? null : summary.Name) { Details = includeDetails ? summary : null };
    }
}

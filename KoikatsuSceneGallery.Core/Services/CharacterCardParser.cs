using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class CharacterCardParser
{
    public static CharacterMetadata? TryParse(string filePath) => Read(filePath, false);
    public static CharacterMetadata? TryParseWithDetails(string filePath) => Read(filePath, true);

    private static CharacterMetadata? Read(string filePath, bool includeDetails)
    {
        var summary = CardMetadataReader.TryRead(filePath, includeExtendedData: false)?.Summary;
        if (summary is null || summary.CardType == "KoikatuClothes") return null;
        return new CharacterMetadata(summary.LastName, summary.FirstName, summary.Nickname,
            summary.Sex, summary.Game, summary.IsMadevil) { Details = includeDetails ? summary : null };
    }
}

using System.Globalization;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CardMetadataCsvCompatibilityTests
{
    // KKManager 11b31fcb012c269d6d19c3cf0eef7aae0466ceaf:
    // FileSize.FromBytes truncates to KiB; GetCompactSize selects the unit before rounding.
    [Theory]
    [InlineData(-1L, "0 B")]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "0 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(2047L, "1 KB")]
    [InlineData(1048575L, "1023 KB")]
    [InlineData(1048576L, "1 MB")]
    [InlineData(1572864L, "1.5 MB")]
    [InlineData(1073741823L, "1024 MB")]
    [InlineData(1073741824L, "1 GB")]
    [InlineData(1099511627776L, "1 TB")]
    [InlineData(long.MaxValue, "8388608 TB")]
    public void SizeProjectionMatchesReferenceBoundaries(long bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Assert.Equal(expected, CardMetadataExport.FormatSize(bytes));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("en-US", "1.5 MB")]
    [InlineData("de-DE", "1,5 MB")]
    public void CsvKeepsAllFourteenFieldsWhenSizeUsesLocalDecimalSeparator(string culture, string size)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var document = new CardMetadataDocument("card.png", 1572864, "【KoiKatuCharaSun】",
                new CardMetadataSummary
                {
                    CardType = "KoikatsuSunshine", Name = "Character", Sex = 1,
                    Game = GameVersion.KoikatsuSunshine, PersonalityId = 39,
                    UserId = "creator", DataId = "data", Version = "0.0.0",
                    PluginGuids = ["plugin.a", "plugin.b"], ExtendedSize = 1572864
                }, []);

            // Expected complete projection from CardWindow's metadata export;
            // missing-mod fields are intentionally empty without an installed-mod index.
            Assert.Equal(
                "\"FileName\",\"Size\",\"CardType\",\"CharacterName\",\"Sex\",\"Personality\",\"CreatorID\",\"DataID\",\"Version\",\"ExtendedDataCount\",\"ExtendedSize\",\"MissingZipmods\",\"MissingPlugins\",\"MissingPluginsMaybe\"\r\n" +
                $"\"card.png\",\"{size}\",\"KoikatsuSunshine\",\"Character\",\"Female\",\"Island Girl\",\"creator\",\"data\",\"0.0.0\",\"2\",\"{size}\",\"\",\"\",\"\"\r\n\r\n",
                CardMetadataExport.ToCsv(document));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}

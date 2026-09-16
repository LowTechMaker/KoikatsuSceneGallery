using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

public static class CardMetadataExport
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, MaxDepth = 256,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    public const string CsvHeader = "FileName,Size,CardType,CharacterName,Sex,Personality,CreatorID,DataID,Version,ExtendedDataCount,ExtendedSize,MissingZipmods,MissingPlugins,MissingPluginsMaybe";

    public static string ToCsv(CardMetadataDocument document)
    {
        var s = document.Summary;
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        // A coordinate keeps the header name as it stands, empty included; only a
        // character name falls back to the file name.
        bool isClothes = s.CardType == "KoikatuClothes";
        string name = isClothes
            ? s.Name ?? ""
            : string.IsNullOrEmpty(s.Name) ? Path.GetFileNameWithoutExtension(document.FileName) : s.Name;
        string[] values = [document.FileName, FormatSize(document.FileSize), s.CardType,
            name,
            s.Sex switch { 0 => "Male", 1 => "Female", _ => "Unknown" },
            isClothes ? "" : PersonalityName(s.PersonalityId, s.Game),
            s.UserId ?? "", s.DataId ?? "", s.Version ?? "",
            s.PluginGuids.Length.ToString(CultureInfo.InvariantCulture), FormatSize(s.ExtendedSize), "", "", ""];
        return string.Join(",", CsvHeader.Split(',').Select(Quote)) + "\r\n" + string.Join(",", values.Select(Quote)) + "\r\n\r\n";
    }

    public static async Task WriteAsync(CardMetadataDocument document, string destination, bool csv, CancellationToken token = default)
    {
        string target = Path.GetFullPath(destination);
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (csv) await File.WriteAllTextAsync(temporary, ToCsv(document), Encoding.Unicode, token);
            else
            {
                await using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, token);
                await stream.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    // KKManager quantizes bytes to whole KiB before selecting the displayed unit.
    public static string FormatSize(long bytes)
    {
        double value = Math.Max(0, bytes) / 1024;
        if (value == 0) return "0 B";
        string[] units = ["KB", "MB", "GB", "TB"];
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return Math.Round((float)value, 2).ToString(CultureInfo.CurrentCulture) + " " + units[unit];
    }

    public static string PersonalityName(int? id, GameVersion game)
    {
        string[] names = ["Sexy", "Ojousama", "Snobby", "Kouhai", "Mysterious", "Weirdo", "Yamato Nadeshiko",
            "Tomboy", "Pure", "Simple", "Delusional", "Motherly", "Big Sisterly", "Gyaru", "Delinquent",
            "Wild", "Wannabe", "Reluctant", "Jinxed", "Bookish", "Timid", "Typical Schoolgirl", "Trendy",
            "Otaku", "Yandere", "Lazy", "Quiet", "Stubborn", "Old-Fashioned", "Humble", "Friendly",
            "Willful", "Honest", "Glamorous", "Returnee", "Slangy", "Sadistic", "Emotionless", "Perfectionist"];
        if (id is null or < 0 or > 90) return "Invalid";
        if (id < names.Length) return names[id.Value];
        if (id == 39 && game == GameVersion.KoikatsuSunshine) return "Island Girl";
        if (id is >= 80 and <= 86) return "Story-only " + id;
        return "Unknown";
    }
}

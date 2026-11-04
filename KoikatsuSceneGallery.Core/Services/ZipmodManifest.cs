using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace KoikatsuSceneGallery.Services;

/// <summary>The game a mod can be installed into.</summary>
public enum ModGame
{
    Koikatsu,
    KoikatsuSunshine,
}

/// <summary>
/// What a Sideloader mod says about itself in its <c>manifest.xml</c>. The GUID is
/// the mod's identity: two files with the same GUID are two copies of one mod, and
/// Sideloader loads only the newest of them.
/// </summary>
public sealed record ZipmodManifest(
    string Guid,
    string? Name,
    string? Version,
    string? Author,
    IReadOnlyList<string> Games)
{
    private static readonly HashSet<string> KoikatsuNames = new(StringComparer.Ordinal)
    {
        "koikatsu", "koikatu", "kk", "koikatsu party", "koikatsuparty", "コイカツ",
    };

    private static readonly HashSet<string> SunshineNames = new(StringComparer.Ordinal)
    {
        "koikatsu sunshine", "koikatsusunshine", "kks", "コイカツ サンシャイン", "コイカツサンシャイン",
    };

    /// <summary>
    /// The games this mod may go into. No game listed means any game. A Koikatsu mod also
    /// goes into Sunshine, which runs Koikatsu content; a Sunshine mod does not go back.
    /// A list naming only other games (AI Shoujo, Emotion Creators…) yields nothing.
    /// </summary>
    public IReadOnlyList<ModGame> SupportedGames()
    {
        if (Games.Count == 0) return [ModGame.Koikatsu, ModGame.KoikatsuSunshine];
        var names = Games.Select(game => game.Trim().ToLowerInvariant()).ToList();
        var result = new List<ModGame>();
        if (names.Any(KoikatsuNames.Contains)) result.Add(ModGame.Koikatsu);
        if (names.Any(KoikatsuNames.Contains) || names.Any(SunshineNames.Contains)) result.Add(ModGame.KoikatsuSunshine);
        return result;
    }
}

public static class ZipmodManifestReader
{
    /// <summary>Extensions Sideloader loads from the mods folder.</summary>
    public static bool HasModExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".zipmod", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads the manifest at the root of the archive. Null when the file is not a zip,
    /// has no manifest, or the manifest has no GUID: such a file is not a mod — an
    /// ordinary .zip of cards looks exactly like that.
    /// </summary>
    public static ZipmodManifest? TryRead(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entry = archive.Entries.FirstOrDefault(e =>
                e.FullName.Equals("manifest.xml", StringComparison.OrdinalIgnoreCase));
            if (entry is null || entry.Length > 1024 * 1024) return null;
            using var stream = entry.Open();
            return Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or XmlException)
        {
            return null;
        }
    }

    internal static ZipmodManifest? Parse(Stream stream)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using var reader = XmlReader.Create(stream, settings);
        var root = XDocument.Load(reader).Root;
        if (root is null) return null;

        string? Value(string name)
        {
            var text = root.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        var guid = Value("guid");
        if (guid is null) return null;
        var games = root.Elements()
            .Where(e => e.Name.LocalName.Equals("game", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Value.Trim())
            .Where(game => game.Length > 0)
            .ToList();
        return new ZipmodManifest(guid, Value("name"), Value("version"), Value("author"), games);
    }
}

/// <summary>
/// Orders mod versions the way people write them: "1.10" is newer than "1.9", and
/// "v1.2" equals "1.2". Pieces are compared one by one, numbers as numbers and the
/// rest as text; a missing piece counts as zero. No version is older than any version.
/// </summary>
public static class ModVersion
{
    public static int Compare(string? left, string? right)
    {
        var a = Split(left);
        var b = Split(right);
        if (a.Count == 0 || b.Count == 0) return a.Count.CompareTo(b.Count);
        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : "0";
            var y = i < b.Count ? b[i] : "0";
            int result;
            if (long.TryParse(x, out var nx) && long.TryParse(y, out var ny)) result = nx.CompareTo(ny);
            else result = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
            if (result != 0) return result;
        }
        return 0;
    }

    private static List<string> Split(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return [];
        var text = version.Trim();
        if (text.Length > 1 && (text[0] == 'v' || text[0] == 'V') && char.IsDigit(text[1])) text = text[1..];
        return text.Split(['.', '-', '_', ' ', '+'], StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}

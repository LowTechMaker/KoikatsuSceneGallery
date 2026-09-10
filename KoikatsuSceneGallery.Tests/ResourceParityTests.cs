using System.Xml.Linq;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// The three resource files are maintained by hand, and a key present in only
/// some of them shows up as blank UI in the languages that lack it, with no
/// build or runtime error to catch it.
/// </summary>
public sealed class ResourceParityTests
{
    private static readonly string[] Languages = ["en-US", "zh-Hans", "zh-Hant"];

    [Fact]
    public void EveryLanguageDefinesTheSameKeys()
    {
        var byLanguage = Languages.ToDictionary(
            language => language,
            language => ReadKeys(language));

        var reference = byLanguage[Languages[0]];
        foreach (var language in Languages.Skip(1))
        {
            var keys = byLanguage[language];
            Assert.Empty(reference.Except(keys, StringComparer.Ordinal));
            Assert.Empty(keys.Except(reference, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void NoLanguageDeclaresAKeyTwiceOrLeavesOneEmpty()
    {
        foreach (var language in Languages)
        {
            var entries = ReadEntries(language);

            Assert.Equal(entries.Count, entries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(entries, entry => string.IsNullOrWhiteSpace(entry.Value));
        }
    }

    private static HashSet<string> ReadKeys(string language)
        => [.. ReadEntries(language).Select(entry => entry.Key)];

    private static List<KeyValuePair<string, string>> ReadEntries(string language)
    {
        var path = ResolvePath(language);
        return
        [
            .. XDocument.Load(path).Root!
                .Elements("data")
                .Where(data => data.Attribute("name") is not null)
                .Select(data => new KeyValuePair<string, string>(
                    data.Attribute("name")!.Value,
                    data.Element("value")?.Value ?? ""))
        ];
    }

    private static string ResolvePath(string language)
    {
        // The test binary sits under bin/<config>/<tfm>; walk up to the folder
        // holding the app project rather than copying resources into output.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Strings", language, "Resources.resw");
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate Strings/{language}/Resources.resw.");
    }
}

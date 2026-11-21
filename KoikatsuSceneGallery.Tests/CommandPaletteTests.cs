using System.Text.RegularExpressions;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class CommandPaletteRankerTests
{
    private static PaletteCandidate<string> C(string title, PaletteCategory category, params string[] keywords)
        => new(title, keywords, category, title);

    [Fact]
    public void EveryTermMustMatch()
    {
        // Typing more should narrow the list, never widen it.
        Assert.True(CommandPaletteRanker.Score("場景 資料夾", "場景卡資料夾", []) > 0);
        Assert.Equal(0, CommandPaletteRanker.Score("場景 快取", "場景卡資料夾", []));
    }

    [Fact]
    public void TitlePrefixBeatsContainsAndTitleBeatsKeywords()
    {
        var prefix = CommandPaletteRanker.Score("篩選", "篩選設定", []);
        var contains = CommandPaletteRanker.Score("篩選", "圖庫篩選", []);
        var keyword = CommandPaletteRanker.Score("篩選", "圖片尺寸", ["只顯示符合篩選的卡片"]);
        Assert.True(prefix > contains);
        Assert.True(contains > keyword);
        Assert.True(keyword > 0);
    }

    [Fact]
    public void AMatchAtAWordStartBeatsOneInsideAWord()
    {
        Assert.True(CommandPaletteRanker.Score("05", "scene_05.png", [])
            > CommandPaletteRanker.Score("05", "scene205.png", []));
    }

    [Fact]
    public void MatchingIgnoresCase()
        => Assert.True(CommandPaletteRanker.Score("PIXIV", "切換到 pixiv", []) > 0);

    [Fact]
    public void AnEmptyQueryListsOnlyTheCommandsInOrder()
    {
        var ranked = CommandPaletteRanker.Rank("  ",
            [C("b", PaletteCategory.Command), C("card", PaletteCategory.Card), C("a", PaletteCategory.Command)],
            perCategory: 5, total: 10);
        Assert.Equal(["b", "a"], ranked.Select(r => r.Payload));
    }

    [Fact]
    public void ManyMatchingCardsCannotCrowdOutCommands()
    {
        // A big library has thousands of file names containing a common word;
        // the command with the same word must still be on the list.
        var cards = Enumerable.Range(0, 100).Select(i => C($"scene {i}", PaletteCategory.Card));
        var ranked = CommandPaletteRanker.Rank("scene",
            [.. cards, C("Open scene gallery", PaletteCategory.Command)], perCategory: 6, total: 12);
        Assert.Contains(ranked, r => r.Category == PaletteCategory.Command);
        Assert.Equal(6, ranked.Count(r => r.Category == PaletteCategory.Card));
    }

    [Fact]
    public void TiesGoToTheEarlierCategory()
    {
        var ranked = CommandPaletteRanker.Rank("tags",
            [C("tags", PaletteCategory.Card), C("tags", PaletteCategory.Command)], perCategory: 5, total: 5);
        Assert.Equal(PaletteCategory.Command, ranked[0].Category);
    }
}

public class SettingsSearchIndexTests
{
    /// <summary>
    /// The index is written out by hand, so it is checked against the page:
    /// every card and section heading, on the tab it is on, and nothing else.
    /// </summary>
    [Fact]
    public void TheIndexMatchesTheSettingsPage()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "Pages", "SettingsPage.xaml"));
        var tabStarts = Regex.Matches(xaml, @"<appControls:SettingsTab\b").Select(m => m.Index).Append(xaml.Length).ToArray();
        Assert.Equal(SettingsSearchIndex.TabKeys.Count, tabStarts.Length - 1);

        var onPage = new HashSet<(string, int, bool)>();
        for (var tab = 0; tab < tabStarts.Length - 1; tab++)
        {
            var section = xaml[tabStarts[tab]..tabStarts[tab + 1]];
            foreach (Match m in Regex.Matches(section, @"<controls:SettingsCard\s[^>]*?x:Uid=""([^""]+)""", RegexOptions.Singleline))
                onPage.Add((m.Groups[1].Value, tab, true));
            foreach (Match m in Regex.Matches(section, @"<TextBlock\s[^>]*?x:Uid=""([A-Za-z_]+Header)""", RegexOptions.Singleline))
                onPage.Add((m.Groups[1].Value, tab, false));
        }

        var indexed = SettingsSearchIndex.Entries.Select(e => (e.Uid, e.Tab, e.IsCard)).ToHashSet();
        Assert.Empty(onPage.Except(indexed));
        Assert.Empty(indexed.Except(onPage));
    }

    [Fact]
    public void EveryLabelExistsInEveryLanguage()
    {
        foreach (var language in new[] { "en-US", "zh-Hans", "zh-Hant" })
        {
            var resw = File.ReadAllText(Path.Combine(RepoRoot(), "Strings", language, "Resources.resw"));
            foreach (var key in SettingsSearchIndex.Entries.Select(e => e.LabelKey).Concat(SettingsSearchIndex.TabKeys))
                Assert.Contains($"name=\"{key}\"", resw);
        }
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "KoikatsuSceneGallery.sln"))) return dir.FullName;
        }

        throw new InvalidOperationException("Repository root not found.");
    }
}

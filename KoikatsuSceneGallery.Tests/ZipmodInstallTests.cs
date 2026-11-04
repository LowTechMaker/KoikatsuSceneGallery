using System.IO.Compression;
using System.Text;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ZipmodInstallTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("zipmod").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Path_(string relative) => Path.GetFullPath(Path.Combine(_root, relative));

    private string Mod(string relative, string guid, string? version, params string[] games)
    {
        var xml = new StringBuilder("<manifest schema-ver=\"1\">");
        xml.Append($"<guid>{guid}</guid><name>{guid} name</name><author>someone</author>");
        if (version is not null) xml.Append($"<version>{version}</version>");
        foreach (var game in games) xml.Append($"<game>{game}</game>");
        xml.Append("</manifest>");
        return Zip(relative, ("manifest.xml", xml.ToString()), ("abdata/list.csv", guid));
    }

    private string Zip(string relative, params (string Name, string Text)[] entries)
    {
        var path = Path_(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(text);
        }
        return path;
    }

    private ModInstallTarget Target(ModGame game) => new(
        game, Path_($"{game}/mods"), Path_($"{game}/mods/SceneGallery"));

    private static ModInstallCandidate Candidate(string path) => new(path, ZipmodManifestReader.TryRead(path));

    private IReadOnlyList<ModInstallStep> Plan(IEnumerable<string> sources, params ModGame[] games)
    {
        var targets = games.Select(Target).ToList();
        var indexes = targets.ToDictionary(t => t.Game, t => InstalledModIndex.Build(t.ModsDirectory, ZipmodManifestReader.TryRead, CancellationToken.None));
        return ModInstallPlanner.Plan(sources.Select(Candidate).ToList(), targets, game => indexes[game]);
    }

    [Fact]
    public void TheManifestIsReadAndAZipOfCardsIsNotAMod()
    {
        var mod = Mod("in/a.zipmod", "com.a", "1.2", "Koikatsu");
        var cards = Zip("in/cards.zip", ("card.png", "x"));
        File.WriteAllText(Path_("in/broken.zipmod"), "not a zip");

        var manifest = ZipmodManifestReader.TryRead(mod)!;
        Assert.Equal(("com.a", "1.2", "someone"), (manifest.Guid, manifest.Version, manifest.Author));
        Assert.Equal(["Koikatsu"], manifest.Games);
        Assert.Null(ZipmodManifestReader.TryRead(cards));
        Assert.Null(ZipmodManifestReader.TryRead(Path_("in/broken.zipmod")));
    }

    [Theory]
    [InlineData("1.10", "1.9", 1)]
    [InlineData("v1.2", "1.2", 0)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("1.2.1", "1.2", 1)]
    [InlineData(null, "0.1", -1)]
    [InlineData(null, null, 0)]
    [InlineData("1.0b", "1.0a", 1)]
    public void VersionsCompareByPiece(string? left, string? right, int expected)
        => Assert.Equal(expected, Math.Sign(ModVersion.Compare(left, right)));

    [Fact]
    public void GamesDecideWhereAModGoes()
    {
        ModGame[] both = [ModGame.Koikatsu, ModGame.KoikatsuSunshine];
        Assert.Equal(both, new ZipmodManifest("g", null, null, null, []).SupportedGames());
        Assert.Equal(both, new ZipmodManifest("g", null, null, null, ["Koikatsu"]).SupportedGames());
        Assert.Equal([ModGame.KoikatsuSunshine], new ZipmodManifest("g", null, null, null, ["Koikatsu Sunshine"]).SupportedGames());
        Assert.Empty(new ZipmodManifest("g", null, null, null, ["AI-Shoujo"]).SupportedGames());
    }

    [Fact]
    public void AModLeftWithAZipExtensionIsRecognisedByItsManifestAndInstalled()
    {
        // Authors often do not rename a mod to .zipmod: the manifest, not the extension, decides.
        var lazy = Mod("in/lazy.zip", "com.lazy", "1.0");
        var cards = Zip("in/cards.zip", ("card.png", "x"));
        Assert.True(ZipmodManifestReader.HasModExtension(lazy));

        var steps = Plan([lazy, cards], ModGame.Koikatsu);

        Assert.Equal([ModInstallAction.Install, ModInstallAction.NotAMod], steps.Select(s => s.Action));
        Assert.True(Assert.Single(ModInstaller.Execute(steps, Path_("backup"), null, DateTimeOffset.UnixEpoch)).Succeeded);
        Assert.Equal("com.lazy", ZipmodManifestReader.TryRead(Path_("Koikatsu/mods/SceneGallery/lazy.zip"))!.Guid);
    }

    [Fact]
    public void ANewModIsInstalledIntoTheGallerysFolderAndTheSourceIsKept()
    {
        var source = Mod("in/a.zipmod", "com.a", "1.0");

        var steps = Plan([source], ModGame.Koikatsu);
        var step = Assert.Single(steps);
        Assert.Equal(ModInstallAction.Install, step.Action);

        var outcome = Assert.Single(ModInstaller.Execute(steps, Path_("backup"), Path_("log.jsonl"), DateTimeOffset.UnixEpoch));
        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(Path_("Koikatsu/mods/SceneGallery/a.zipmod")));
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.EnumerateFiles(Path_("Koikatsu/mods"), "*.sgtmp", SearchOption.AllDirectories));
        Assert.Contains("com.a", File.ReadAllText(Path_("log.jsonl")));
    }

    [Fact]
    public void ASameOrNewerCopyAnywhereInModsIsLeftAlone()
    {
        Mod("Koikatsu/mods/MyMods/same.zipmod", "com.same", "1.0");
        Mod("Koikatsu/mods/Sideloader Modpack/newer.zipmod", "com.newer", "2.0");

        var steps = Plan([Mod("in/same.zipmod", "com.same", "1.0"), Mod("in/newer.zipmod", "com.newer", "1.5")], ModGame.Koikatsu);

        Assert.Equal([ModInstallAction.AlreadyInstalled, ModInstallAction.NewerInstalled], steps.Select(s => s.Action));
        Assert.Empty(ModInstaller.Execute(steps, Path_("backup"), null, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void AnOlderCopyOfTheUsersIsKeptButOneTheGalleryInstalledIsBackedUp()
    {
        var users = Mod("Koikatsu/mods/MyMods/old.zipmod", "com.a", "1.0");
        var ours = Mod("Koikatsu/mods/SceneGallery/a.zipmod", "com.a", "1.1");

        var steps = Plan([Mod("in/a.zipmod", "com.a", "1.2")], ModGame.Koikatsu);
        var step = Assert.Single(steps);
        Assert.Equal(ModInstallAction.Update, step.Action);
        Assert.Equal(2, step.OlderCopies.Count);
        Assert.Equal(ours, Assert.Single(step.ToBackUp).Path);
        Assert.Equal(ours, step.DestinationPath);

        var outcome = Assert.Single(ModInstaller.Execute(steps, Path_("backup"), null, DateTimeOffset.UnixEpoch));
        Assert.True(outcome.Succeeded);
        Assert.True(File.Exists(users));
        Assert.Equal("1.2", ZipmodManifestReader.TryRead(ours)!.Version);
        Assert.Equal("1.1", ZipmodManifestReader.TryRead(Assert.Single(outcome.BackedUpTo))!.Version);
    }

    [Fact]
    public void TheNewestOfOneModInTheBatchWinsAndNamesDoNotCollide()
    {
        Mod("Koikatsu/mods/SceneGallery/x.zipmod", "com.other", "1.0");
        var older = Mod("in/1/x.zipmod", "com.a", "1.0");
        var newer = Mod("in/2/x.zipmod", "com.a", "1.1");
        var different = Mod("in/3/x.zipmod", "com.b", "1.0");

        var steps = Plan([older, newer, different], ModGame.Koikatsu);

        Assert.Equal(ModInstallAction.SupersededInBatch, steps[0].Action);
        Assert.Equal(Path_("Koikatsu/mods/SceneGallery/x (2).zipmod"), steps[1].DestinationPath);
        Assert.Equal(Path_("Koikatsu/mods/SceneGallery/x (3).zipmod"), steps[2].DestinationPath);
    }

    [Fact]
    public void ASunshineModIsNotInstalledIntoKoikatsu()
    {
        var steps = Plan([Mod("in/s.zipmod", "com.s", "1", "KoikatsuSunshine")], ModGame.Koikatsu, ModGame.KoikatsuSunshine);

        Assert.Equal(
            [(ModGame.Koikatsu, ModInstallAction.NotForThisGame), (ModGame.KoikatsuSunshine, ModInstallAction.Install)],
            steps.Select(s => (s.Game!.Value, s.Action)));
    }

    [Fact]
    public void AFailedInstallPutsTheBackedUpCopyBack()
    {
        var ours = Mod("Koikatsu/mods/SceneGallery/a.zipmod", "com.a", "1.0");
        var source = Mod("in/a.zipmod", "com.a", "2.0");
        var steps = Plan([source], ModGame.Koikatsu);

        // Holding the destination's temporary file open makes the copy fail.
        using (File.Open(ours + ".sgtmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            var outcome = Assert.Single(ModInstaller.Execute(steps, Path_("backup"), null, DateTimeOffset.UnixEpoch));
            Assert.False(outcome.Succeeded);
        }
        Assert.Equal("1.0", ZipmodManifestReader.TryRead(ours)!.Version);
    }
}

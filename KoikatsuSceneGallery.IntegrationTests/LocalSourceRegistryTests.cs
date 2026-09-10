using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.IntegrationTests;

/// <summary>
/// Covers the built-in local provider against a real directory layout: the
/// folder shapes it must claim, the ones it must leave alone, and the fact
/// that nothing it answers leaves the machine.
/// </summary>
public sealed class LocalSourceRegistryTests
{
    private const string ImportSubfolder = "Organized";
    private const string LocalFolderName = "Local";

    private sealed class Logger : IAppLogger
    {
        public List<string> Errors { get; } = [];

        public void LogError(string operation, Exception exception, string? path = null)
            => Errors.Add(operation);
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "ksg-local-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateSourceFolder(string folderName, string game = "Koikatsu", string rating = "R-18")
        {
            var path = System.IO.Path.Combine(Path, ImportSubfolder, LocalFolderName, game, rating, folderName);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static (LocalSourceRegistry Registry, LocalSourceProvider Provider, Logger Logger) Build(
        params string[] roots)
    {
        var logger = new Logger();
        var registry = new LocalSourceRegistry(logger);
        registry.UpdateConfiguration(roots, ImportSubfolder, LocalFolderName);
        return (registry, new LocalSourceProvider(registry), logger);
    }

    [Fact]
    public async Task Rescan_FindsSourcesNestedUnderTheGameAndRatingFolders()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, logger) = Build(root.Path);

        await registry.RescanAsync();

        var source = Assert.Single(registry.Sources);
        Assert.Equal("local-k7f3q9", source.Id);
        Assert.Equal("阿明", source.DisplayName);
        Assert.Equal(directory, source.Directory);
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task Rescan_FindsASourceThatHasNoCardsYet()
    {
        using var root = new TempRoot();
        root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, _) = Build(root.Path);

        await registry.RescanAsync();

        Assert.Single(registry.Sources);
    }

    [Fact]
    public async Task Rescan_IgnoresFoldersOutsideTheLocalScope()
    {
        using var root = new TempRoot();
        Directory.CreateDirectory(Path.Combine(root.Path, ImportSubfolder, "pixiv", "作者 (local-k7f3q9)"));
        Directory.CreateDirectory(Path.Combine(root.Path, "阿明 (local-aaabbb)"));
        var (registry, _, _) = Build(root.Path);

        await registry.RescanAsync();

        Assert.Empty(registry.Sources);
    }

    [Fact]
    public async Task Rescan_IgnoresARemoteProvidersAuthorFolder()
    {
        using var root = new TempRoot();
        root.CreateSourceFolder("someone (12345678)");
        var (registry, _, _) = Build(root.Path);

        await registry.RescanAsync();

        Assert.Empty(registry.Sources);
    }

    // The description is authoritative for the name, so renaming a source in
    // the app must not require moving its folder or any card.
    [Fact]
    public async Task StoredDescription_OverridesTheFolderDerivedName()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, provider, _) = Build(root.Path);
        await registry.EnsureDocumentAsync(directory, "local-k7f3q9", "小明");

        await registry.RescanAsync();

        Assert.Equal("小明", registry.Find("local-k7f3q9")?.DisplayName);

        var info = await provider.GetAuthorInfoAsync(
            new AuthorKey(LocalSourceIdentity.ProviderId, "local-k7f3q9"),
            forceRefresh: false,
            CancellationToken.None);
        Assert.Equal("小明", info?.Name);
        Assert.True(Directory.Exists(directory));
        Assert.Equal("阿明 (local-k7f3q9)", Path.GetFileName(directory));
    }

    [Fact]
    public async Task StoredDescription_IsIgnoredWhenItsIdDisagreesWithTheFolder()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, _) = Build(root.Path);
        await registry.EnsureDocumentAsync(directory, "local-zzzzzz", "別人");

        await registry.RescanAsync();

        var source = Assert.Single(registry.Sources, s => s.Id == "local-k7f3q9");
        Assert.Equal("阿明", source.DisplayName);
    }

    [Fact]
    public async Task RenameAsync_MovesTheFolderAndKeepsTheIdentity()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var card = Path.Combine(directory, "card.png");
        await File.WriteAllBytesAsync(card, [1, 2, 3]);
        var (registry, provider, _) = Build(root.Path);
        await registry.RescanAsync();

        var result = await registry.RenameAsync("local-k7f3q9", "小明");

        var moved = Path.Combine(Path.GetDirectoryName(directory)!, "小明 (local-k7f3q9)");
        Assert.False(Directory.Exists(directory));
        Assert.True(File.Exists(Path.Combine(moved, "card.png")));
        Assert.Equal(1, result.FoldersRenamed);
        Assert.Empty(result.FoldersLeftBehind);
        Assert.Equal("小明", registry.Find("local-k7f3q9")?.DisplayName);
        Assert.Equal(moved, registry.Find("local-k7f3q9")?.Directory);
        Assert.Equal("小明", new LocalSourceStore().Read(moved)?.DisplayName);

        // The folder is what carries the identity, so the moved one must still
        // resolve to the same author.
        Assert.Equal(
            "local-k7f3q9",
            provider.TryParseFolderName(Path.GetFileName(moved))?.Key.Id);
    }

    // A second rename has to find the folder where the first one left it.
    [Fact]
    public async Task RenameAsync_TwiceInARowFollowsTheFolderItJustMoved()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, logger) = Build(root.Path);
        await registry.RescanAsync();

        await registry.RenameAsync("local-k7f3q9", "小明");
        var second = await registry.RenameAsync("local-k7f3q9", "夜羽");

        Assert.Equal(1, second.FoldersRenamed);
        Assert.True(Directory.Exists(
            Path.Combine(Path.GetDirectoryName(directory)!, "夜羽 (local-k7f3q9)")));
        Assert.Empty(logger.Errors);
    }

    [Fact]
    public async Task RenameAsync_CanLeaveTheFolderAloneWhenAskedTo()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        var result = await registry.RenameAsync("local-k7f3q9", "小明", renameFolders: false);

        Assert.True(Directory.Exists(directory));
        Assert.Equal(0, result.FoldersRenamed);
        Assert.Equal("小明", new LocalSourceStore().Read(directory)?.DisplayName);
    }

    // A folder already carrying the new name is not merged into: the source is
    // still renamed everywhere the app reads it, and the user is told.
    [Fact]
    public async Task RenameAsync_ReportsAFolderItCouldNotMove()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        root.CreateSourceFolder("小明 (local-k7f3q9)");
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        var result = await registry.RenameAsync("local-k7f3q9", "小明");

        Assert.True(Directory.Exists(directory));
        Assert.Equal(0, result.FoldersRenamed);
        Assert.Equal("小明", registry.Find("local-k7f3q9")?.DisplayName);
    }

    [Fact]
    public async Task SetAvatarAsync_CopiesThePictureBesideTheCardsAndClearingRemovesIt()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var picture = Path.Combine(root.Path, "face.png");
        await File.WriteAllBytesAsync(picture, [9, 8, 7]);
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        var withAvatar = await registry.SetAvatarAsync("local-k7f3q9", picture);

        Assert.NotNull(withAvatar?.AvatarPath);
        Assert.True(File.Exists(withAvatar.AvatarPath));
        Assert.Equal([9, 8, 7], await File.ReadAllBytesAsync(withAvatar.AvatarPath!));
        // Stored inside the source, not referenced where the user picked it.
        Assert.StartsWith(directory, withAvatar.AvatarPath!, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(withAvatar.AvatarPath)!, "*.tmp"));

        var cleared = await registry.ClearAvatarAsync("local-k7f3q9");

        Assert.Null(cleared?.AvatarPath);
        Assert.False(File.Exists(withAvatar.AvatarPath));
    }

    // The picture lives in the folder, so the rename must not lose it.
    [Fact]
    public async Task AnAvatarSurvivesARename()
    {
        using var root = new TempRoot();
        root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var picture = Path.Combine(root.Path, "face.jpg");
        await File.WriteAllBytesAsync(picture, [4, 5, 6]);
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();
        await registry.SetAvatarAsync("local-k7f3q9", picture);

        await registry.RenameAsync("local-k7f3q9", "小明");

        var renamed = registry.Find("local-k7f3q9");
        Assert.NotNull(renamed?.AvatarPath);
        Assert.True(File.Exists(renamed.AvatarPath));
        Assert.Contains("小明", renamed.AvatarPath!, StringComparison.Ordinal);
    }

    // Replacing a picture with one of another format must not leave the first
    // file behind for GetAvatarPath to find.
    [Fact]
    public async Task ReplacingAnAvatarWithAnotherFormatLeavesNoOrphan()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var png = Path.Combine(root.Path, "a.png");
        var jpg = Path.Combine(root.Path, "b.jpg");
        await File.WriteAllBytesAsync(png, [1]);
        await File.WriteAllBytesAsync(jpg, [2]);
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        await registry.SetAvatarAsync("local-k7f3q9", png);
        var second = await registry.SetAvatarAsync("local-k7f3q9", jpg);

        Assert.EndsWith(".jpg", second?.AvatarPath, StringComparison.Ordinal);
        var metadata = Path.GetDirectoryName(second!.AvatarPath)!;
        Assert.Single(Directory.EnumerateFiles(metadata, "avatar.*"));
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public async Task SetAvatarAsync_IgnoresAFileFormatItCannotShow()
    {
        using var root = new TempRoot();
        root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var document = Path.Combine(root.Path, "notes.txt");
        await File.WriteAllTextAsync(document, "not a picture");
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        var result = await registry.SetAvatarAsync("local-k7f3q9", document);

        Assert.Null(result?.AvatarPath);
    }

    [Fact]
    public async Task PendingSource_SurvivesARescanThatFindsNoFolderForIt()
    {
        using var root = new TempRoot();
        var (registry, _, _) = Build(root.Path);
        var pending = registry.CreatePending("阿明");

        await registry.RescanAsync();

        var source = Assert.Single(registry.Sources);
        Assert.Equal(pending.Id, source.Id);
        Assert.Null(source.Directory);
    }

    // Refreshing every author in turn must not cost one full scan per author.
    [Fact]
    public async Task ReloadAsync_RereadsOneKnownSourceWithoutRescanning()
    {
        using var root = new TempRoot();
        var directory = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, provider, _) = Build(root.Path);
        await registry.RescanAsync();

        await new LocalSourceStore().UpdateAsync(directory, "local-k7f3q9", "小明");
        var second = root.CreateSourceFolder("別人 (local-aaabbb)");

        var reloaded = await registry.ReloadAsync("local-k7f3q9");

        Assert.Equal("小明", reloaded?.DisplayName);
        // The unrelated folder added meanwhile stays unseen: no rescan ran.
        Assert.Null(registry.Find("local-aaabbb"));
        Assert.True(Directory.Exists(second));

        var info = await provider.GetAuthorInfoAsync(
            new AuthorKey(LocalSourceIdentity.ProviderId, "local-k7f3q9"),
            forceRefresh: true,
            CancellationToken.None);
        Assert.Equal("小明", info?.Name);
    }

    [Fact]
    public async Task ReloadAsync_FallsBackToARescanForAnUnknownId()
    {
        using var root = new TempRoot();
        root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var (registry, _, _) = Build(root.Path);

        Assert.Equal("阿明", (await registry.ReloadAsync("local-k7f3q9"))?.DisplayName);
    }

    [Fact]
    public async Task Provider_ClaimsOnlyTheStrictLocalFolderMarker()
    {
        var (_, provider, _) = Build();

        Assert.Equal(LocalSourceIdentity.ProviderId, provider.ProviderId);

        var parsed = provider.TryParseFolderName("阿明 (local-k7f3q9)");
        Assert.NotNull(parsed);
        Assert.Equal("local-k7f3q9", parsed.Key.Id);
        Assert.Equal("阿明", parsed.FolderDisplayName);

        Assert.Null(provider.TryParseFolderName("someone (12345678)"));
        Assert.Null(provider.TryParseFolderName("阿明"));

        await Task.CompletedTask;
    }

    // A local card has no remote artwork identity by design: inventing one
    // would pollute artwork grouping, the folder index and sidecar names.
    [Fact]
    public async Task Provider_NeverResolvesAnArtworkOrAProfileUrl()
    {
        var (_, provider, _) = Build();
        var artworkId = new ArtworkId(LocalSourceIdentity.ProviderId, "whatever");

        Assert.Null(provider.TryParseFilename("card_12345678.png"));
        Assert.Null(provider.TryParseArtworkFolderName("Title (12345678)"));
        Assert.Null(provider.TryParseUrl("https://www.pixiv.net/artworks/12345678"));
        Assert.Null(await provider.FetchArtworkInfoAsync(artworkId, CancellationToken.None));
        Assert.Equal("", provider.GetArtworkUrl(artworkId));
        Assert.Equal("", provider.GetProfileUrl(new AuthorKey(LocalSourceIdentity.ProviderId, "local-k7f3q9")));
    }

    [Fact]
    public async Task Provider_ReturnsNothingForAnUnknownOrForeignKey()
    {
        var (_, provider, _) = Build();

        Assert.Null(await provider.GetAuthorInfoAsync(
            new AuthorKey(LocalSourceIdentity.ProviderId, "local-zzzzzz"), false, CancellationToken.None));
        Assert.Null(await provider.GetAuthorInfoAsync(
            new AuthorKey("pixiv", "12345678"), false, CancellationToken.None));
    }

    // Rating folders must stay on: the gallery derives R-18 from the path, so
    // without them "hide R-18" would silently miss every local card.
    [Fact]
    public void Provider_KeepsRatingFoldersAndFollowsTheConfiguredScopeName()
    {
        var (registry, provider, _) = Build();

        Assert.True(provider.UsesRatingFolders);
        Assert.Equal(LocalFolderName, provider.DestinationFolderName);

        registry.UpdateConfiguration([], ImportSubfolder, "本地");
        Assert.Equal("本地", provider.DestinationFolderName);
    }

    // A source owns one folder per library root, because a card lands under the
    // root for its own type. That is normal, not a conflict.
    [Fact]
    public async Task ASourceSpanningSeveralFoldersIsOneSourceAndNotAnError()
    {
        using var root = new TempRoot();
        var scenes = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var characters = root.CreateSourceFolder("阿明 (local-k7f3q9)", rating: "G");
        var (registry, _, logger) = Build(root.Path);

        await registry.RescanAsync();

        Assert.Single(registry.Sources);
        Assert.Empty(logger.Errors);
        Assert.Equal(
            [characters, scenes],
            registry.DirectoriesOf("local-k7f3q9").OrderBy(d => d, StringComparer.Ordinal));
    }

    // Otherwise the name would depend on which root happened to be scanned
    // first on the next start.
    [Fact]
    public async Task RenamingASourceReachesEveryFolderItOwns()
    {
        using var root = new TempRoot();
        var scenes = root.CreateSourceFolder("阿明 (local-k7f3q9)");
        var characters = root.CreateSourceFolder("阿明 (local-k7f3q9)", rating: "G");
        var (registry, _, _) = Build(root.Path);
        await registry.RescanAsync();

        var result = await registry.RenameAsync("local-k7f3q9", "小明");

        Assert.Equal(2, result.FoldersRenamed);
        Assert.False(Directory.Exists(scenes));
        Assert.False(Directory.Exists(characters));

        var store = new LocalSourceStore();
        foreach (var directory in registry.DirectoriesOf("local-k7f3q9"))
        {
            Assert.Equal("小明 (local-k7f3q9)", Path.GetFileName(directory));
            Assert.Equal("小明", store.Read(directory)?.DisplayName);
        }

        Assert.Equal(2, registry.DirectoriesOf("local-k7f3q9").Count);
    }
}

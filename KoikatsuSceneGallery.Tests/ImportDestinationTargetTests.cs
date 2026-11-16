using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// <see cref="ImportDestinationPolicy.ResolveTarget"/>: the one place that
/// decides which folder a card goes to, for import and for giving a library
/// card its source later.
/// </summary>
/// <remarks>
/// New folders use the three-level layout (<c>Organized\provider\author\</c>,
/// no game or rating folder). Folders that already exist are found in either
/// layout and reused, so a library that has not been moved keeps filling its
/// old folders.
/// </remarks>
public sealed class ImportDestinationTargetTests
{
    private static readonly string Root = Path.Combine("lib", "scenes");
    private static readonly string Organized = Path.Combine(Root, "Organized");
    private static readonly (string Folder, bool UsesRatingFolders) Pixiv = ("Pixiv", true);

    private static ImportTargetInput Input(
        bool hasArtwork = false, string? authorId = null, string? authorName = null,
        string? authorProviderId = null, (string Folder, bool UsesRatingFolders)? scope = null,
        IReadOnlyList<string>? roots = null, Func<string, bool>? directoryExists = null)
        => new(
            Roots: roots ?? [Root],
            ImportSubfolder: "Organized",
            UnknownFolderName: "Unknown",
            GameVersionFolder: "KKS",
            GameVersionFolderNames: ["KK", "KKS", ""],
            Scope: scope ?? ("", true),
            RatingFolder: "R-18",
            ProviderScopes: [Pixiv],
            HasArtwork: hasArtwork,
            ProviderId: authorProviderId ?? (scope is null ? null : "pixiv"),
            AuthorId: authorId,
            AuthorName: authorName,
            AuthorProviderId: authorProviderId,
            FormatAuthorFolder: (name, id) => $"{name} ({id})",
            DirectoryExists: directoryExists ?? (_ => false));

    private static ImportTargetInput PixivAuthor(bool hasArtwork = true)
        => Input(hasArtwork: hasArtwork, authorId: "42", authorName: "New name", authorProviderId: "pixiv", scope: Pixiv);

    private static Dictionary<string, Dictionary<string, string>> NoIndex() => [];

    /// <summary>An index holding <paramref name="directory"/> for author 42 under (game, rating).</summary>
    private static Dictionary<string, Dictionary<string, string>> Index(params (string Game, string Rating, string Directory)[] folders)
    {
        var index = new Dictionary<string, Dictionary<string, string>>();
        foreach (var (game, rating, directory) in folders)
            index[ImportLibraryIndexer.BuildScopeKey(Root, "Pixiv", game, rating)] = new() { ["42"] = directory };
        return index;
    }

    // --- new folders: three levels ---

    [Fact]
    public void ACardWithNoAuthorGoesToTheUnknownSinkSoItKeepsItsOwnTile()
    {
        var target = ImportDestinationPolicy.ResolveTarget(Input(), NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Unknown", GalleryGrouping.UnrecognizedFolderName), target.Folder);
        Assert.Null(target.AuthorDirectory);
        // Two such cards no longer share one gallery key.
        Assert.NotEqual(
            GalleryGrouping.GetKey(Path.Combine(target.Folder, "a.png")),
            GalleryGrouping.GetKey(Path.Combine(target.Folder, "b.png")));
    }

    [Fact]
    public void AKnownPostWhoseAuthorIsUnknownAlsoGoesToTheSink()
    {
        // The fetch failed: an artwork id but no author. Its sidecar still
        // belongs in the Unknown folder itself, not in the sink.
        var target = ImportDestinationPolicy.ResolveTarget(Input(hasArtwork: true, scope: Pixiv), NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Unknown", GalleryGrouping.UnrecognizedFolderName), target.Folder);
        Assert.Equal(Path.Combine(Organized, "Unknown"), target.AuthorDirectory);
    }

    [Fact]
    public void AnAuthorIdWithoutANameAlsoGoesToTheSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "42", authorProviderId: "pixiv", scope: Pixiv), NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Unknown", GalleryGrouping.UnrecognizedFolderName), target.Folder);
    }

    [Fact]
    public void ANewAuthorFolderHasNoGameOrRatingFolder()
    {
        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), NoIndex())!;

        var author = Path.Combine(Organized, "Pixiv", "New name (42)");
        Assert.Equal(author, target.Folder);
        Assert.Equal(author, target.AuthorDirectory);
    }

    [Fact]
    public void AKnownAuthorWithoutAPostGoesToTheAuthorsSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(hasArtwork: false), NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Pixiv", "New name (42)", GalleryGrouping.UnrecognizedFolderName), target.Folder);
        Assert.Null(target.AuthorDirectory);
    }

    [Fact]
    public void AnAuthorWithoutAProviderGetsAFolderUnderUnknownAndNoSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "someone", authorName: "Someone"), NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Unknown", "someone"), target.Folder);
    }

    [Fact]
    public void ALocalAuthorIsNotPutInTheSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "7", authorName: "Me", authorProviderId: LocalSourceIdentity.ProviderId,
                scope: ("Local", false)),
            NoIndex())!;

        Assert.Equal(Path.Combine(Organized, "Local", "Me (7)"), target.Folder);
    }

    [Fact]
    public void NoRootForTheCardTypeMeansNoTarget()
        => Assert.Null(ImportDestinationPolicy.ResolveTarget(Input(roots: []), NoIndex()));

    // --- existing folders are found in either layout ---

    [Fact]
    public void AnOldLayoutAuthorFolderIsReused()
    {
        var old = Path.Combine(Organized, "Pixiv", "KKS", "R-18", "Old name (42)");

        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), Index(("KKS", "R-18", old)))!;

        Assert.Equal(old, target.Folder);
        Assert.Equal(old, target.AuthorDirectory);
    }

    [Fact]
    public void ANewLayoutAuthorFolderIsReused()
    {
        var current = Path.Combine(Organized, "Pixiv", "Old name (42)");

        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), Index(("", "", current)))!;

        Assert.Equal(current, target.Folder);
    }

    [Fact]
    public void WithBothLayoutsPresentTheNewOneWins()
    {
        var old = Path.Combine(Organized, "Pixiv", "KKS", "R-18", "Old name (42)");
        var current = Path.Combine(Organized, "Pixiv", "Old name (42)");

        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), Index(("KKS", "R-18", old), ("", "", current)))!;

        Assert.Equal(current, target.Folder);
    }

    [Fact]
    public void AnOldLayoutFolderUnderAnotherGameButTheSameRatingIsReused()
    {
        // The game no longer separates authors, the rating still does: an
        // R-18 card may join the author's R-18 folder of the other game.
        var otherGame = Path.Combine(Organized, "Pixiv", "KK", "R-18", "Old name (42)");

        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), Index(("KK", "R-18", otherGame)))!;

        Assert.Equal(otherGame, target.Folder);
    }

    [Fact]
    public void AnOldLayoutFolderUnderAnotherRatingIsNotReused()
    {
        // Putting an R-18 card in the author's G folder would make its folder
        // say G. It gets a new-layout folder instead, and its rating is filed.
        var general = Path.Combine(Organized, "Pixiv", "KKS", "G", "Old name (42)");

        var target = ImportDestinationPolicy.ResolveTarget(PixivAuthor(), Index(("KKS", "G", general)))!;

        Assert.Equal(Path.Combine(Organized, "Pixiv", "New name (42)"), target.Folder);
    }

    [Fact]
    public void AnOldLayoutFolderFoundByTheFallbackIsReused()
    {
        // No post: the existing fallback across providers still applies.
        var old = Path.Combine(Organized, "Pixiv", "KKS", "R-18", "Old name (42)");
        var input = Input(authorId: "42", authorName: "New name", authorProviderId: "pixiv", scope: ("Elsewhere", true));

        var target = ImportDestinationPolicy.ResolveTarget(input, Index(("KKS", "R-18", old)))!;

        Assert.Equal(Path.Combine(old, GalleryGrouping.UnrecognizedFolderName), target.Folder);
    }

    [Fact]
    public void AFallbackThatOnlyNamesTheProviderBuildsANewLayoutFolderThere()
    {
        // The author exists under another game folder of the Pixiv scope with
        // a scope that is not the card's own: the provider is reused, the
        // folder is new and three levels deep.
        var otherGame = Path.Combine(Organized, "Pixiv", "KK", "G", "Old name (42)");
        var input = Input(authorId: "42", authorName: "New name", authorProviderId: "pixiv", scope: ("Elsewhere", true));
        var index = new Dictionary<string, Dictionary<string, string>>
        {
            [ImportLibraryIndexer.BuildScopeKey(Root, "Pixiv", "KK", "R-18")] = new() { ["42"] = otherGame },
        };

        var target = ImportDestinationPolicy.ResolveTarget(input, index)!;

        Assert.Equal(Path.Combine(Organized, "Pixiv", "New name (42)", GalleryGrouping.UnrecognizedFolderName), target.Folder);
    }

    [Fact]
    public void AnOldUnknownAuthorFolderIsReused()
    {
        var old = Path.Combine(Organized, "Unknown", "KKS", "someone");

        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "someone", authorName: "Someone", directoryExists: path => path == old), NoIndex())!;

        Assert.Equal(old, target.Folder);
    }

    [Fact]
    public void ANewUnknownAuthorFolderWinsOverAnOldOne()
    {
        var old = Path.Combine(Organized, "Unknown", "KKS", "someone");
        var current = Path.Combine(Organized, "Unknown", "someone");

        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "someone", authorName: "Someone", directoryExists: path => path == old || path == current),
            NoIndex())!;

        Assert.Equal(current, target.Folder);
    }
}

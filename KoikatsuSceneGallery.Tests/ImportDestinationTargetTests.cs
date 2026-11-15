using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// <see cref="ImportDestinationPolicy.ResolveTarget"/>: the one place that
/// decides which folder a card goes to, for import and for giving a library
/// card its source later.
/// </summary>
public sealed class ImportDestinationTargetTests
{
    private static readonly string Root = Path.Combine("lib", "scenes");
    private static readonly (string Folder, bool UsesRatingFolders) Pixiv = ("Pixiv", true);

    private static ImportTargetInput Input(
        bool hasArtwork = false, string? authorId = null, string? authorName = null,
        string? authorProviderId = null, (string Folder, bool UsesRatingFolders)? scope = null,
        IReadOnlyList<string>? roots = null)
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
            FormatAuthorFolder: (name, id) => $"{name} ({id})");

    private static Dictionary<string, Dictionary<string, string>> NoIndex() => [];

    [Fact]
    public void ACardWithNoAuthorGoesToTheUnknownSinkSoItKeepsItsOwnTile()
    {
        var target = ImportDestinationPolicy.ResolveTarget(Input(), NoIndex())!;

        var expected = Path.Combine(Root, "Organized", "Unknown", "KKS", GalleryGrouping.UnrecognizedFolderName);
        Assert.Equal(expected, target.Folder);
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

        var unknown = Path.Combine(Root, "Organized", "Unknown", "KKS");
        Assert.Equal(Path.Combine(unknown, GalleryGrouping.UnrecognizedFolderName), target.Folder);
        Assert.Equal(unknown, target.AuthorDirectory);
    }

    [Fact]
    public void AnAuthorIdWithoutANameAlsoGoesToTheSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "42", authorProviderId: "pixiv", scope: Pixiv), NoIndex())!;

        Assert.Equal(
            Path.Combine(Root, "Organized", "Unknown", "KKS", GalleryGrouping.UnrecognizedFolderName),
            target.Folder);
    }

    [Fact]
    public void AKnownAuthorAndPostGoToTheAuthorFolder()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(hasArtwork: true, authorId: "42", authorName: "Name", authorProviderId: "pixiv", scope: Pixiv),
            NoIndex())!;

        var author = Path.Combine(Root, "Organized", "Pixiv", "KKS", "R-18", "Name (42)");
        Assert.Equal(author, target.Folder);
        Assert.Equal(author, target.AuthorDirectory);
    }

    [Fact]
    public void AKnownAuthorWithoutAPostGoesToTheAuthorsSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "42", authorName: "Name", authorProviderId: "pixiv", scope: Pixiv), NoIndex())!;

        Assert.Equal(
            Path.Combine(Root, "Organized", "Pixiv", "KKS", "R-18", "Name (42)", GalleryGrouping.UnrecognizedFolderName),
            target.Folder);
        Assert.Null(target.AuthorDirectory);
    }

    [Fact]
    public void AnAuthorWithoutAProviderGetsAFolderUnderUnknownAndNoSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "someone", authorName: "Someone"), NoIndex())!;

        Assert.Equal(Path.Combine(Root, "Organized", "Unknown", "KKS", "someone"), target.Folder);
    }

    [Fact]
    public void ALocalAuthorIsNotPutInTheSink()
    {
        var target = ImportDestinationPolicy.ResolveTarget(
            Input(authorId: "7", authorName: "Me", authorProviderId: LocalSourceIdentity.ProviderId,
                scope: ("Local", false)),
            NoIndex())!;

        Assert.Equal(Path.Combine(Root, "Organized", "Local", "KKS", "Me (7)"), target.Folder);
    }

    [Fact]
    public void AnExistingAuthorFolderIsReused()
    {
        var existing = Path.Combine(Root, "Organized", "Pixiv", "KKS", "R-18", "Old name (42)");
        var index = new Dictionary<string, Dictionary<string, string>>
        {
            [ImportLibraryIndexer.BuildScopeKey(Root, "Pixiv", "KKS", "R-18")] = new() { ["42"] = existing },
        };

        var target = ImportDestinationPolicy.ResolveTarget(
            Input(hasArtwork: true, authorId: "42", authorName: "New name", authorProviderId: "pixiv", scope: Pixiv),
            index)!;

        Assert.Equal(existing, target.Folder);
    }

    [Fact]
    public void NoRootForTheCardTypeMeansNoTarget()
        => Assert.Null(ImportDestinationPolicy.ResolveTarget(Input(roots: []), NoIndex()));
}

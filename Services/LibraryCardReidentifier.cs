using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.ViewModels;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>What the user said about a library card's source.</summary>
/// <param name="FilePath">A card inside a library root.</param>
/// <param name="Artwork">The post, when known.</param>
/// <param name="AuthorName">Required, with the id, when <paramref name="Artwork"/> is null.</param>
/// <param name="Rating">Null takes the post's rating, or All ages without a post.</param>
internal sealed record ReidentifyRequest(
    string FilePath,
    ArtworkId? Artwork,
    string? AuthorName, string? AuthorId, string? AuthorProviderId,
    ContentRating? Rating);

/// <param name="Problem">Why it cannot be applied; null when it can.</param>
/// <param name="AuthorDirectory">Where the post sidecar goes (the import rules' author folder).</param>
internal sealed record ReidentifyPlan(
    ReidentifyRequest Request, string SourcePath, string DestinationPath,
    ArtworkInfo? Fetched, bool WritesSidecar, ReidentifyProblem? Problem,
    string? AuthorDirectory = null);

/// <summary>
/// Gives a card already in the library its post and author afterwards: moves
/// it where an import with that source would have put it, writes the post
/// sidecar, and takes its rating and state along.
/// </summary>
/// <remarks>
/// The destination comes from <see cref="ImportService.ResolveLibraryCardDestinationAsync"/>,
/// the import rules themselves, so the library layout is decided in one
/// place. The guards and the order of the steps are
/// <see cref="LibraryCardRelocation"/>'s and <see cref="LibraryCardMover"/>'s,
/// which are tested in Core; this class only gathers what they need.
/// </remarks>
internal sealed class LibraryCardReidentifier(
    ImportService importService,
    SettingsViewModel settings,
    UserCardRatingStore ratings,
    CardStateService cardStates,
    IAppLogger logger)
{
    private readonly LibraryCardMover _mover = new();

    private string[] Roots() =>
        [.. settings.FolderPaths, .. settings.CharacterFolderPaths, .. settings.CoordinateFolderPaths];

    public async Task<ReidentifyPlan> PlanAsync(ReidentifyRequest request, CancellationToken ct)
    {
        var source = Path.GetFullPath(request.FilePath);
        var roots = Roots();
        var hasAuthor = !string.IsNullOrWhiteSpace(request.AuthorId) && !string.IsNullOrWhiteSpace(request.AuthorName);
        if (LibraryCardRelocation.Check(source, roots, request.Artwork is not null, hasAuthor) is { } refused)
            return Refuse(refused);

        ArtworkInfo? fetched = null;
        if (request.Artwork is { } artwork)
        {
            fetched = await importService.FetchArtworkInfoAsync(artwork, ct).ConfigureAwait(false);
            if (fetched is null) return Refuse(ReidentifyProblem.PostNotFound);
        }

        var item = await Task.Run(() => BuildItem(source, request, fetched), ct).ConfigureAwait(false);
        var destination = await importService.ResolveLibraryCardDestinationAsync(item, ct).ConfigureAwait(false);
        if (destination is null || LibraryCardRelocation.CheckDestination(destination, roots) is not null)
            return Refuse(ReidentifyProblem.NotInLibrary);

        var writesSidecar = fetched is not null && item.AuthorDirectoryPath is not null;
        return new ReidentifyPlan(request, source, destination, fetched, writesSidecar, null, item.AuthorDirectoryPath);

        ReidentifyPlan Refuse(ReidentifyProblem problem)
            => new(request, source, "", null, false, problem);
    }

    public async Task<LibraryCardMoveResult> ApplyAsync(ReidentifyPlan plan, CancellationToken ct)
    {
        if (plan.Problem is not null || string.IsNullOrEmpty(plan.DestinationPath))
            return new LibraryCardMoveResult(
                LibraryCardMoveOutcome.Failed, plan.SourcePath, $"The plan cannot be applied: {plan.Problem}.");

        // "Has this card a filed rating already?" must read the files, not an
        // empty store: answering no would overwrite the rating it has.
        await cardStates.EnsureLoadedAsync().ConfigureAwait(false);

        var steps = new LibraryCardRelocationSteps(
            ComputeHash: (path, token) => CardPayloadHash.TryCompute(path, token),
            Mover: _mover,
            WriteSidecar: (newPath, token) =>
            {
                var item = BuildItem(plan.SourcePath, plan.Request, plan.Fetched);
                item.DestinationPath = newPath;
                item.AuthorDirectoryPath = plan.AuthorDirectory;
                return importService.WriteLibraryCardSidecarAsync(item, token);
            },
            FollowCard: async (newPath, hash, token) =>
            {
                try { await ratings.ResolveAsync(newPath, hash, token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogError("Reidentify.FollowRating", ex, newPath);
                }
                await cardStates.ResolveMovedAsync(newPath, hash).ConfigureAwait(false);
            },
            // The old path is gone and the new one is not in the import page's
            // index: both would mislead the next "already in the library" check.
            LibraryChanged: (_, _) => importService.InvalidateLibraryFileCache(),
            FolderRatingOf: FolderRatingOf,
            FiledRatingOf: cardStates.FiledRatingOf,
            SetFiledRating: (path, hash, rating, _) => cardStates.SetFiledRatingAsync(path, hash, rating));

        return await LibraryCardRelocation.ApplyAsync(plan.SourcePath, plan.DestinationPath, plan.WritesSidecar, steps, ct,
            chosenRating: plan.Request.Rating is { } chosen ? (int)chosen : null).ConfigureAwait(false);
    }

    /// <summary>The rating the card's current rating folders imply, by the user's folder names.</summary>
    private int FolderRatingOf(string path)
    {
        static HashSet<string> Names(string name) => string.IsNullOrWhiteSpace(name)
            ? new(StringComparer.OrdinalIgnoreCase)
            : new([name.Trim()], StringComparer.OrdinalIgnoreCase);
        return SceneRating.FromFolders(path, Names(settings.R18FolderName), Names(settings.R18GFolderName));
    }

    /// <summary>The card as the import rules see it: type and game from the file, the rest from the source.</summary>
    private static ImportItem BuildItem(string source, ReidentifyRequest request, ArtworkInfo? fetched)
    {
        var (cardType, gameVersion) = CardTypeClassifier.ClassifyExtended(source);
        return new ImportItem
        {
            SourceFilePath = source,
            Status = ImportItemStatus.ReadyToImport,
            CardType = cardType,
            GameVersion = gameVersion,
            ArtworkId = request.Artwork,
            FetchedArtworkInfo = fetched,
            AuthorName = fetched?.AuthorName ?? request.AuthorName,
            AuthorId = fetched?.AuthorId ?? request.AuthorId,
            AuthorProviderId = request.Artwork?.ProviderId ?? request.AuthorProviderId,
            Title = fetched?.Title,
            Tags = fetched?.Tags,
            Rating = request.Rating ?? fetched?.Rating ?? ContentRating.AllAges,
        };
    }
}

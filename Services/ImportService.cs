using System.Collections.ObjectModel;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Orchestrates the import pipeline: card type classification, pixiv API
/// fetch (author + tags + R-18), destination folder resolution, and file move.
/// </summary>
public sealed class ImportService
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    private readonly ICardImportProvider _importProvider;
    private readonly IFolderAuthorProvider? _authorProvider;
    private readonly SettingsService _settingsService;

    public ImportService(
        ICardImportProvider importProvider,
        IFolderAuthorProvider? authorProvider,
        SettingsService settingsService)
    {
        _importProvider = importProvider;
        _authorProvider = authorProvider;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Analyzes a batch of dropped files: classifies card type, parses artwork
    /// IDs, fetches artwork info from the provider. Items in the collection are
    /// updated on the dispatcher thread as results arrive.
    /// </summary>
    public async Task<int> AnalyzeAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct)
    {
        // Phase 1: classify card type + parse artwork ID (all local, fast)
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            item.Status = ImportItemStatus.Analyzing;
            item.CardType = CardTypeClassifier.Classify(item.SourceFilePath);
            item.ArtworkId = _importProvider.TryParseFilename(item.FileName);

            if (item.CardType == CardType.NotACard)
                item.Status = ImportItemStatus.Skipped;
        }

        // Remove non-card files from the queue
        var nonCards = items.Where(i => i.CardType == CardType.NotACard).ToList();
        int rejectedCount = nonCards.Count;
        if (rejectedCount > 0)
        {
            dispatcher.TryEnqueue(() =>
            {
                foreach (var nc in nonCards)
                    items.Remove(nc);
            });
        }

        // Phase 2: deduplicate artwork IDs and fetch from provider
        var groups = items
            .Where(i => i.ArtworkId is not null)
            .GroupBy(i => i.ArtworkId!.Id)
            .ToList();

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();

            var artworkId = group.First().ArtworkId!;
            ArtworkInfo? info;
            try
            {
                info = await _importProvider.FetchArtworkInfoAsync(artworkId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                info = null;
            }

            dispatcher.TryEnqueue(() =>
            {
                foreach (var item in group)
                {
                    if (info is not null)
                    {
                        item.AuthorName = info.AuthorName;
                        item.AuthorId = info.AuthorId;
                        item.Rating = info.Rating;
                        item.Tags = info.Tags;
                    }

                    if (item.Status != ImportItemStatus.Skipped)
                        item.Status = ImportItemStatus.ReadyToImport;
                }
            });
        }

        // Mark remaining items without artwork ID as ready (they're valid cards but no pixiv ID)
        dispatcher.TryEnqueue(() =>
        {
            foreach (var item in items.Where(i => i.ArtworkId is null && i.Status == ImportItemStatus.Analyzing))
                item.Status = ImportItemStatus.ReadyToImport;
        });

        // Phase 3: resolve destinations
        var config = await _settingsService.LoadConfigAsync().ConfigureAwait(false);
        dispatcher.TryEnqueue(() => ResolveDestinations(items, config));

        return rejectedCount;
    }

    /// <summary>
    /// Moves all ReadyToImport (non-excluded) items to their resolved destinations.
    /// </summary>
    public async Task ImportAsync(
        ObservableCollection<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken ct)
    {
        var toImport = items
            .Where(i => i.Status == ImportItemStatus.ReadyToImport && !i.IsExcluded && i.DestinationPath is not null)
            .ToList();

        foreach (var item in toImport)
        {
            ct.ThrowIfCancellationRequested();
            dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Importing);

            try
            {
                await Task.Run(() =>
                {
                    var destDir = Path.GetDirectoryName(item.DestinationPath!)!;
                    Directory.CreateDirectory(destDir);

                    if (File.Exists(item.DestinationPath))
                    {
                        dispatcher.TryEnqueue(() =>
                        {
                            item.Status = ImportItemStatus.Skipped;
                            item.ErrorMessage = "File already exists";
                        });
                        return;
                    }

                    File.Move(item.SourceFilePath, item.DestinationPath!);
                    dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Completed);
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                dispatcher.TryEnqueue(() =>
                {
                    item.Status = ImportItemStatus.Failed;
                    item.ErrorMessage = ex.Message;
                });
            }
        }
    }

    private void ResolveDestinations(ObservableCollection<ImportItem> items, SettingsService.ConfigData config)
    {
        // Build folder index: for each configured root, enumerate subdirectories
        // and parse author IDs using the folder name parser.
        var folderIndex = BuildFolderIndex(config);

        foreach (var item in items)
        {
            if (item.Status != ImportItemStatus.ReadyToImport || item.CardType == CardType.NotACard)
                continue;

            var roots = GetRootsForCardType(item.CardType, config);
            if (roots.Count == 0) continue;

            string? targetFolder = null;

            if (item.AuthorId is not null)
            {
                // Search all roots for this card type for a matching author folder
                foreach (var root in roots)
                {
                    if (folderIndex.TryGetValue(root, out var authorFolders)
                        && authorFolders.TryGetValue(item.AuthorId, out var existing))
                    {
                        targetFolder = existing;
                        break;
                    }
                }

                // No existing folder — create under the first root
                if (targetFolder is null && item.AuthorName is not null)
                {
                    var safeName = SanitizeFolderName($"{item.AuthorName} ({item.AuthorId})");
                    targetFolder = Path.Combine(roots[0], safeName);
                }
            }

            // Fallback: use first root directly if no author info
            targetFolder ??= roots[0];

            item.DestinationPath = Path.Combine(targetFolder, item.FileName);
        }
    }

    private Dictionary<string, Dictionary<string, string>> BuildFolderIndex(SettingsService.ConfigData config)
    {
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var allRoots = config.FolderPaths
            .Concat(config.CharacterFolderPaths)
            .Concat(config.CoordinateFolderPaths)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in allRoots)
        {
            if (!Directory.Exists(root)) continue;

            var authorFolders = new Dictionary<string, string>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var folderName = Path.GetFileName(dir);
                    var parsed = _authorProvider?.TryParseFolderName(folderName);
                    if (parsed is not null)
                        authorFolders.TryAdd(parsed.Key.Id, dir);
                }
            }
            catch
            {
                // Inaccessible directory — skip
            }

            index[root] = authorFolders;
        }

        return index;
    }

    private static List<string> GetRootsForCardType(CardType cardType, SettingsService.ConfigData config)
    {
        return cardType switch
        {
            CardType.Scene => config.FolderPaths,
            CardType.Character => config.CharacterFolderPaths,
            CardType.Coordinate => config.CoordinateFolderPaths,
            _ => [],
        };
    }

    private static string SanitizeFolderName(string name)
    {
        foreach (var c in InvalidFileNameChars)
            name = name.Replace(c, '_');
        return name.Trim();
    }
}

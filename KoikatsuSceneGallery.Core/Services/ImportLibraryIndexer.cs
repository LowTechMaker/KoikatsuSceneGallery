using System.Diagnostics;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

internal sealed record ImportLibrarySource(string SourceFilePath, string FileName);

// ParseAuthorId runs on the indexing worker. Null means unrecognized; cancellation
// exceptions propagate, other failures retain the existing per-directory logging policy.
internal sealed record ImportLibraryProviderScope(
    string Folder, IReadOnlyList<string> RatingFolders, Func<string, string?> ParseAuthorId);

// Callers supply detached snapshots and do not mutate them while BuildAsync is running.
internal sealed record ImportLibraryIndexRequest(
    IReadOnlyList<ImportLibrarySource> Sources,
    IReadOnlyList<string> Roots,
    string ImportSubfolder,
    IReadOnlyList<string> GameVersionFolders,
    IReadOnlyList<ImportLibraryProviderScope> ProviderScopes);

internal sealed record ImportLibraryIndexResult(
    HashSet<string> IdenticalSourcePaths,
    Dictionary<string, Dictionary<string, string>> FolderIndex,
    long LibraryScanAndCandidateIndexElapsedMs,
    long ByteComparisonElapsedMs,
    long FolderIndexElapsedMs,
    int TotalCandidatesCompared,
    int ActualIdenticalDuplicates,
    int ActualContentMismatches);

// Builds detached indexes only; it never changes workspace items or performs imports.
internal sealed class ImportLibraryIndexer(
    LibraryFileCache cache, Action<string, Exception, string?> logError)
{
    /// <summary>
    /// How many files may share one copy-normalized name before the payload
    /// comparison is abandoned for that name.
    /// </summary>
    /// <remarks>
    /// A file the shell copied a few times gives two or three. Anything much
    /// larger means the normalization was wrong, and the cost of finding out by
    /// reading is unbounded — every file in the bucket, hashed, for one card.
    /// </remarks>
    private const int MaxCopyBucket = 8;

    private readonly LibraryFileCache _cache = cache;
    private readonly Action<string, Exception, string?> _logError = logError;

    public Task<ImportLibraryIndexResult> BuildAsync(
        ImportLibraryIndexRequest request,
        CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            var roots = request.Roots;
            var cachedIndex = await _cache.GetOrBuildAsync(
                roots,
                token => BuildExistingFileIndex(roots, token),
                cancellationToken).ConfigureAwait(false);

            var byteComparisonStopwatch = Stopwatch.StartNew();
            var duplicateDetection = FindIdenticalSourcePaths(
                request.Sources, cachedIndex.FilesByName, cancellationToken);
            byteComparisonStopwatch.Stop();

            var folderIndexStopwatch = Stopwatch.StartNew();
            var folders = BuildFolderIndex(request, cancellationToken);
            folderIndexStopwatch.Stop();

            return new ImportLibraryIndexResult(
                duplicateDetection.IdenticalSourcePaths,
                folders,
                cachedIndex.ScanElapsedMilliseconds,
                byteComparisonStopwatch.ElapsedMilliseconds,
                folderIndexStopwatch.ElapsedMilliseconds,
                duplicateDetection.TotalCandidatesCompared,
                duplicateDetection.ActualIdenticalDuplicates,
                duplicateDetection.ActualContentMismatches);
        }, cancellationToken);

    private Dictionary<string, List<string>> BuildExistingFileIndex(
        IReadOnlyList<string> roots,
        CancellationToken cancellationToken)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.png", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fileName = Path.GetFileName(file);
                    if (!index.TryGetValue(fileName, out var paths))
                    {
                        paths = [];
                        index[fileName] = paths;
                    }
                    paths.Add(file);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _logError("Import.ScanExistingFilenames", ex, root); }
        }

        return index;
    }

    private DuplicateDetectionResult FindIdenticalSourcePaths(
        IReadOnlyList<ImportLibrarySource> items,
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingFilesByName,
        CancellationToken cancellationToken)
    {
        var identicalSourcePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalCandidatesCompared = 0;
        var actualIdenticalDuplicates = 0;
        var actualContentMismatches = 0;

        // Built only if some file needs it, from the cached name index rather
        // than a second walk of the library.
        Dictionary<string, List<string>>? filesByNormalizedName = null;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.SourceFilePath))
                continue;

            var matched = false;
            if (existingFilesByName.TryGetValue(item.FileName, out var candidates))
            {
                foreach (var candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        totalCandidatesCompared++;
                        if (ImportDuplicateDetector.AreFilesIdentical(
                                item.SourceFilePath,
                                candidate,
                                cancellationToken))
                        {
                            actualIdenticalDuplicates++;
                            identicalSourcePaths.Add(item.SourceFilePath);
                            matched = true;
                            break;
                        }

                        actualContentMismatches++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logError(
                            "Import.CompareExistingFile",
                            ex,
                            $"{item.SourceFilePath} | {candidate}");
                    }
                }
            }

            if (matched)
                continue;

            // Nothing of that exact name matched. A copy the file system
            // renamed — "card (1).png" beside "card.png" — is never even
            // looked at above, so it would import as a second card however
            // identical it is. Compare the card data of the files whose name
            // differs only by a copy marker.
            filesByNormalizedName ??= BuildNormalizedNameIndex(existingFilesByName);
            var normalized = CopySuffix.Normalize(item.FileName);
            if (normalized.Length == 0
                || !filesByNormalizedName.TryGetValue(normalized, out var relatives)
                || relatives.Count > MaxCopyBucket)
            {
                // A bucket this large is not a file copied a few times; it means
                // the name normalized into something many unrelated files share.
                // Reading them all would cost the whole library, so the pass is
                // skipped rather than trusted.
                continue;
            }

            // Hashed once for the item, not once per candidate: the source is
            // the same file every time round the loop. Deferred until a
            // candidate survives the skip guards below, because a bucket that
            // holds only the exact-name entries already compared above would
            // otherwise cost a full read of the source for nothing.
            string? sourceHash = null;

            foreach (var candidate in relatives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(candidate, item.SourceFilePath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        Path.GetFileName(candidate),
                        item.FileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Already compared byte for byte above.
                    continue;
                }

                sourceHash ??= CardPayloadHash.TryCompute(item.SourceFilePath, cancellationToken);
                if (sourceHash is null)
                    break;

                try
                {
                    totalCandidatesCompared++;
                    if (string.Equals(
                            sourceHash,
                            CardPayloadHash.TryCompute(candidate, cancellationToken),
                            StringComparison.Ordinal))
                    {
                        actualIdenticalDuplicates++;
                        identicalSourcePaths.Add(item.SourceFilePath);
                        break;
                    }

                    actualContentMismatches++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logError(
                        "Import.CompareCardPayload",
                        ex,
                        $"{item.SourceFilePath} | {candidate}");
                }
            }
        }

        return new DuplicateDetectionResult(
            identicalSourcePaths,
            totalCandidatesCompared,
            actualIdenticalDuplicates,
            actualContentMismatches);
    }

    /// <summary>
    /// The library index re-keyed by name with copy markers removed, so every
    /// file that could be a copy of one name is reachable from it.
    /// </summary>
    private static Dictionary<string, List<string>> BuildNormalizedNameIndex(
        IReadOnlyDictionary<string, IReadOnlyList<string>> existingFilesByName)
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (fileName, paths) in existingFilesByName)
        {
            var normalized = CopySuffix.Normalize(fileName);
            if (normalized.Length == 0)
                continue;

            if (!index.TryGetValue(normalized, out var grouped))
            {
                grouped = [];
                index[normalized] = grouped;
            }

            grouped.AddRange(paths);
        }

        return index;
    }

    private Dictionary<string, Dictionary<string, string>> BuildFolderIndex(
        ImportLibraryIndexRequest request,
        CancellationToken cancellationToken)
    {
        // (root, providerFolder, gameVersionFolder, ratingFolder) → authorId → fullFolderPath
        var index = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var subfolder = request.ImportSubfolder.Trim();
        var allRoots = request.Roots;
        var providerScopes = request.ProviderScopes;

        foreach (var root in allRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            foreach (var providerScope in providerScopes)
            {
                foreach (var gameVersionFolder in request.GameVersionFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var ratingFolder in providerScope.RatingFolders)
                    {
                        var ratingDir = ImportDestinationPolicy.BuildTargetBase(root, subfolder, providerScope.Folder, gameVersionFolder, ratingFolder, null);
                        var authorFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        if (Directory.Exists(ratingDir))
                        {
                            try
                            {
                                foreach (var dir in Directory.EnumerateDirectories(ratingDir))
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    var parsed = providerScope.ParseAuthorId(Path.GetFileName(dir));
                                    if (parsed is not null)
                                        authorFolders.TryAdd(parsed, dir);
                                }
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex) { _logError("Import.BuildFolderIndex", ex, ratingDir); }
                        }

                        index[BuildScopeKey(root, providerScope.Folder, gameVersionFolder, ratingFolder)] = authorFolders;
                    }
                }
            }
        }

        return index;
    }

    private sealed record DuplicateDetectionResult(
        HashSet<string> IdenticalSourcePaths,
        int TotalCandidatesCompared,
        int ActualIdenticalDuplicates,
        int ActualContentMismatches);

    internal static string BuildScopeKey(
        string root,
        string providerFolder,
        string gameVersionFolder,
        string ratingFolder)
        => string.Join('\u001F', root, providerFolder, gameVersionFolder, ratingFolder);

}

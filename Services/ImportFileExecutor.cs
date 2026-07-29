using System.Collections.Concurrent;
using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.ApplicationModel.Resources;

namespace KoikatsuSceneGallery.Services;

internal sealed class ImportFileExecutor(IAppLogger logger)
{
    private static readonly ResourceLoader ResourceLoader = new();

    private readonly int _maxConcurrency = Math.Clamp(Environment.ProcessorCount, 1, 4);

    public async Task ExecuteAsync(
        IReadOnlyList<ImportItem> items,
        DispatcherQueue dispatcher,
        CancellationToken cancellationToken,
        Func<ImportItem, CancellationToken, Task>? onCompleted = null)
    {
        var completedSourceDirectories = new ConcurrentDictionary<string, byte>(
            StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = _maxConcurrency,
            },
            async (item, token) =>
            {
                token.ThrowIfCancellationRequested();
                dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Importing);
                var completed = false;

                try
                {
                    var destination = item.DestinationPath!;
                    var destinationDirectory = Path.GetDirectoryName(destination)!;
                    Directory.CreateDirectory(destinationDirectory);

                    var destinationExists = File.Exists(destination);
                    var identical = destinationExists
                        && ImportDuplicateDetector.AreFilesIdentical(
                            item.SourceFilePath,
                            destination,
                            token);
                    var conflict = ImportDestinationPolicy.ClassifyFileConflict(
                        destinationExists,
                        identical);

                    switch (conflict)
                    {
                        case ImportFileConflict.Duplicate:
                            File.Delete(item.SourceFilePath);
                            completedSourceDirectories.TryAdd(item.SourceFolder, 0);
                            dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Completed);
                            completed = true;
                            break;
                        case ImportFileConflict.Collision:
                            dispatcher.TryEnqueue(() =>
                            {
                                item.Status = ImportItemStatus.Skipped;
                                item.ErrorMessage = ResourceLoader.GetString(
                                    "Import_ErrorFileAlreadyExists");
                            });
                            break;
                        default:
                            File.Move(item.SourceFilePath, destination);
                            completedSourceDirectories.TryAdd(item.SourceFolder, 0);
                            dispatcher.TryEnqueue(() => item.Status = ImportItemStatus.Completed);
                            completed = true;
                            break;
                    }

                    if (completed && onCompleted is not null)
                        await onCompleted(item, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError("Import.MoveFile", ex, item.SourceFilePath);
                    dispatcher.TryEnqueue(() =>
                    {
                        item.Status = ImportItemStatus.Failed;
                        item.ErrorMessage = ex.Message;
                    });
                }

            });

        await Task.Run(() => CleanupEmptyDirectories(
            completedSourceDirectories.Keys,
            cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    private void CleanupEmptyDirectories(
        IEnumerable<string> directories,
        CancellationToken cancellationToken)
    {
        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex)
            {
                logger.LogError("Import.DeleteEmptySourceDirectory", ex, directory);
            }
        }
    }

}

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Executes a reviewed import plan as an in-memory, all-or-nothing transaction.
/// It intentionally has no dependency on the application UI or plugin contracts.
/// </summary>
internal sealed class ImportTransactionExecutor
{
    private const int MaxRenameAttempts = 1000;

    private readonly PostMetadataStore _metadataStore;

    public ImportTransactionExecutor(PostMetadataStore metadataStore)
    {
        _metadataStore = metadataStore ?? throw new ArgumentNullException(nameof(metadataStore));
    }

    public async Task<ImportExecutionReceipt> ExecuteTransactionAsync(
        IReadOnlyList<ImportItemPlan> plans,
        IProgress<ImportExecutionProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plans);

        var receipts = new List<ImportItemTransactionReceipt>(plans.Count);
        var duplicateReceiptIndexes = new List<int>();
        var createdDestinationDirectories = new HashSet<string>(PathComparer);
        var warnings = new List<ImportExecutionWarning>();
        var sourceDirectories = new HashSet<string>(PathComparer);
        var completedCount = 0;
        var successCount = 0;
        var failedCount = 0;
        var cancelled = false;

        foreach (var plan in plans)
        {
            var receipt = CreatePreparedReceipt(plan);
            if (cancellationToken.IsCancellationRequested)
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.Cancelled,
                });
                cancelled = true;
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }

            if (!File.Exists(plan.SourceFilePath))
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.SourceFileNotFound,
                });
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }

            if (receipt.RequiresSidecarAssociation
                && string.IsNullOrWhiteSpace(receipt.AuthorDirectoryPath))
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.SidecarWriteFault,
                });
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }

            try
            {
                EnsureDestinationDirectory(plan.IdealDestinationPath, createdDestinationDirectories);

                var conflict = TryClassifyConflict(
                    plan.SourceFilePath,
                    plan.IdealDestinationPath,
                    cancellationToken);
                if (conflict == ImportFileConflict.Duplicate)
                {
                    receipts.Add(receipt);
                    duplicateReceiptIndexes.Add(receipts.Count - 1);
                    completedCount++;
                    successCount++;
                    Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                        successCount, failedCount, 0, warnings.Count, receipt);
                    continue;
                }

                if (conflict == ImportFileConflict.Collision)
                {
                    receipts.Add(receipt with
                    {
                        FinalState = TransactionItemState.Failed,
                        FailureType = TransactionFailureType.DestinationContentMismatch,
                    });
                    failedCount++;
                    completedCount++;
                    Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                        successCount, failedCount, 0, warnings.Count, receipts[^1]);
                    break;
                }

                try
                {
                    File.Move(plan.SourceFilePath, plan.IdealDestinationPath);
                }
                catch (IOException) when (File.Exists(plan.IdealDestinationPath)
                    && File.Exists(plan.SourceFilePath))
                {
                    var raceConflict = TryClassifyConflict(
                        plan.SourceFilePath,
                        plan.IdealDestinationPath,
                        cancellationToken);
                    if (raceConflict == ImportFileConflict.Duplicate)
                    {
                        receipts.Add(receipt);
                        duplicateReceiptIndexes.Add(receipts.Count - 1);
                        completedCount++;
                        successCount++;
                        Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                            successCount, failedCount, 0, warnings.Count, receipt);
                        continue;
                    }

                    throw new DestinationContentMismatchException();
                }

                receipt = receipt with
                {
                    FinalState = TransactionItemState.FileMoved,
                    LastDurableState = TransactionItemState.FileMoved,
                };

                if (plan.Document is not null)
                {
                    var document = plan.Document with
                    {
                        LocalFileNames = [Path.GetFileName(plan.IdealDestinationPath)],
                    };
                    var sidecarReceipt = await _metadataStore.WriteWithReceiptAsync(
                        receipt.AuthorDirectoryPath!,
                        document,
                        cancellationToken).ConfigureAwait(false);
                    receipt = receipt with
                    {
                        FinalState = TransactionItemState.SidecarCommitted,
                        LastDurableState = TransactionItemState.SidecarCommitted,
                        SidecarReceipt = sidecarReceipt,
                    };
                }
                else
                {
                    // SidecarCommitted is the terminal committed state. The durable disk
                    // operation for a manual card remains FileMoved, which the planner uses.
                    receipt = receipt with { FinalState = TransactionItemState.SidecarCommitted };
                }

                receipts.Add(receipt);
                sourceDirectories.Add(Path.GetDirectoryName(plan.SourceFilePath)!);
                completedCount++;
                successCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipt);
            }
            catch (OperationCanceledException)
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.Cancelled,
                });
                cancelled = true;
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }
            catch (DestinationContentMismatchException)
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.DestinationContentMismatch,
                });
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }
            catch (Exception exception)
            {
                receipts.Add(receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = receipt.LastDurableState == TransactionItemState.Prepared
                        ? ClassifyMoveFailure(exception)
                        : TransactionFailureType.SidecarWriteFault,
                });
                failedCount++;
                completedCount++;
                Report(progress, ImportExecutionPhase.Executing, completedCount, plans.Count,
                    successCount, failedCount, 0, warnings.Count, receipts[^1]);
                break;
            }
        }

        var forwardSucceeded = !cancelled && failedCount == 0 && completedCount == plans.Count;
        if (!forwardSucceeded)
        {
            var rollback = await RollbackCoreAsync(
                receipts,
                recoveryDirectory: null,
                progress,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            DeleteCreatedEmptyDirectories(createdDestinationDirectories);
            return new ImportExecutionReceipt(
                Guid.NewGuid(),
                DateTimeOffset.Now,
                rollback.Receipts,
                IsFullySuccessful: false)
            {
                Warnings = warnings,
            };
        }

        foreach (var receiptIndex in duplicateReceiptIndexes)
        {
            var receipt = receipts[receiptIndex];
            try
            {
                File.Delete(receipt.SourceFilePath);
                receipts[receiptIndex] = receipt with
                {
                    FinalState = TransactionItemState.DuplicateSourceDeleted,
                    LastDurableState = TransactionItemState.DuplicateSourceDeleted,
                };
                sourceDirectories.Add(Path.GetDirectoryName(receipt.SourceFilePath)!);
            }
            catch (Exception exception)
            {
                warnings.Add(new ImportExecutionWarning(
                    ImportExecutionWarningType.DuplicateSourceCleanupFailed,
                    receipt.SourceFilePath,
                    exception.Message));
            }
        }

        CleanupEmptyDirectories(sourceDirectories);
        var completedReceipt = receipts.Count == 0
            ? CreateEmptyReceipt()
            : receipts[^1];
        Report(progress, ImportExecutionPhase.Completed, plans.Count, plans.Count, successCount,
            failedCount, 0, warnings.Count, completedReceipt,
            warnings.Count == 0 ? ImportExecutionWarningType.None : warnings[^1].Type);

        return new ImportExecutionReceipt(
            Guid.NewGuid(),
            DateTimeOffset.Now,
            receipts,
            IsFullySuccessful: true)
        {
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Rolls back a completed receipt. Missing source directories are deliberately
    /// fail-closed unless the caller supplies an existing recovery directory.
    /// </summary>
    public async Task<ImportExecutionReceipt> RollbackTransactionAsync(
        ImportExecutionReceipt receipt,
        string? userSpecifiedRecoveryFolder,
        IProgress<ImportExecutionProgressSnapshot>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var rollback = await RollbackCoreAsync(
            receipt.ItemReceipts,
            userSpecifiedRecoveryFolder,
            progress,
            cancellationToken).ConfigureAwait(false);
        Report(progress, ImportExecutionPhase.Completed, receipt.ItemReceipts.Count,
            receipt.ItemReceipts.Count, rollback.SuccessCount, rollback.FailedCount,
            rollback.ManualRecoveryRequiredCount, receipt.Warnings.Count,
            rollback.Receipts.Count == 0 ? CreateEmptyReceipt() : rollback.Receipts[^1]);

        return receipt with
        {
            ItemReceipts = rollback.Receipts,
            IsFullySuccessful = rollback.FailedCount == 0
                && rollback.ManualRecoveryRequiredCount == 0,
        };
    }

    private async Task<RollbackResult> RollbackCoreAsync(
        IReadOnlyList<ImportItemTransactionReceipt> originalReceipts,
        string? recoveryDirectory,
        IProgress<ImportExecutionProgressSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        var receipts = new List<ImportItemTransactionReceipt>(originalReceipts.Count);
        var completedCount = 0;
        var successCount = 0;
        var failedCount = 0;
        var manualRecoveryRequiredCount = 0;

        foreach (var originalReceipt in originalReceipts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receipt = originalReceipt;
            var plan = ImportTransactionRollbackPlanner.CreatePlan(receipt);
            if (plan.Action == TransactionRollbackAction.None)
            {
                receipts.Add(receipt);
                completedCount++;
                Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                    originalReceipts.Count, successCount, failedCount,
                    manualRecoveryRequiredCount, 0, receipt);
                continue;
            }

            if (plan.Action == TransactionRollbackAction.ManualRecoveryRequired
                || !TryGetRollbackDirectory(receipt, recoveryDirectory, out var rollbackDirectory)
                || !File.Exists(receipt.TargetFilePath))
            {
                receipt = receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.SourceFileNotFound,
                    RequiresManualRecovery = true,
                };
                receipts.Add(receipt);
                completedCount++;
                manualRecoveryRequiredCount++;
                Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                    originalReceipts.Count, successCount, failedCount,
                    manualRecoveryRequiredCount, 0, receipt);
                continue;
            }

            string movedBackPath;
            try
            {
                movedBackPath = MoveWithRenameRetry(
                    receipt.TargetFilePath,
                    Path.Combine(rollbackDirectory, Path.GetFileName(receipt.TargetFilePath)));
            }
            catch (AtomicRenameExhaustedException)
            {
                receipt = receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.AtomicRenameExhausted,
                    RequiresManualRecovery = true,
                };
                receipts.Add(receipt);
                completedCount++;
                failedCount++;
                Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                    originalReceipts.Count, successCount, failedCount,
                    manualRecoveryRequiredCount, 0, receipt);
                continue;
            }
            catch (Exception)
            {
                receipt = receipt with
                {
                    FinalState = TransactionItemState.Failed,
                    FailureType = TransactionFailureType.FileMoveAccessDenied,
                    RequiresManualRecovery = true,
                };
                receipts.Add(receipt);
                completedCount++;
                failedCount++;
                Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                    originalReceipts.Count, successCount, failedCount,
                    manualRecoveryRequiredCount, 0, receipt);
                continue;
            }

            if (plan.Action == TransactionRollbackAction.MoveFileBackThenUnbindSidecar)
            {
                try
                {
                    foreach (var fileName in receipt.SidecarReceipt!.NewlyAddedFileNames)
                    {
                        await _metadataStore.RemoveLocalFileNameAsync(
                            receipt.AuthorDirectoryPath!,
                            receipt.ProviderId!,
                            receipt.ArtworkId!,
                            fileName,
                            plan.DeleteSidecarIfEmpty,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception)
                {
                    if (!TryCompensateMove(movedBackPath, receipt.TargetFilePath))
                    {
                        receipt = receipt with
                        {
                            FinalState = TransactionItemState.Failed,
                            FailureType = TransactionFailureType.SidecarWriteFault,
                            RequiresManualRecovery = true,
                        };
                        manualRecoveryRequiredCount++;
                    }
                    else
                    {
                        receipt = receipt with
                        {
                            FinalState = TransactionItemState.Failed,
                            FailureType = TransactionFailureType.SidecarWriteFault,
                        };
                        failedCount++;
                    }

                    receipts.Add(receipt);
                    completedCount++;
                    Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                        originalReceipts.Count, successCount, failedCount,
                        manualRecoveryRequiredCount, 0, receipt);
                    continue;
                }
            }

            receipt = receipt with
            {
                SourceFilePath = movedBackPath,
                FinalState = TransactionItemState.Prepared,
                LastDurableState = TransactionItemState.Prepared,
                // Preserve the forward failure in the journal while resetting the
                // durable state. Explicit undo of a committed receipt already has None.
                FailureType = originalReceipt.FailureType,
            };
            receipts.Add(receipt);
            completedCount++;
            successCount++;
            Report(progress, ImportExecutionPhase.RollingBack, completedCount,
                originalReceipts.Count, successCount, failedCount,
                manualRecoveryRequiredCount, 0, receipt);
        }

        return new RollbackResult(receipts, successCount, failedCount, manualRecoveryRequiredCount);
    }

    private static ImportItemTransactionReceipt CreatePreparedReceipt(ImportItemPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new ImportItemTransactionReceipt(
            plan.SourceFilePath,
            plan.IdealDestinationPath,
            plan.Document?.ProviderId,
            plan.Document?.ArtworkId,
            plan.AuthorDirectoryPath,
            RequiresSidecarAssociation: plan.Document is not null,
            TransactionItemState.Prepared,
            TransactionItemState.Prepared,
            TransactionFailureType.None,
            SidecarReceipt: null);
    }

    private static ImportItemTransactionReceipt CreateEmptyReceipt()
        => new(string.Empty, string.Empty, null, null, null, false,
            TransactionItemState.Prepared, TransactionItemState.Prepared,
            TransactionFailureType.None, null);

    private static ImportFileConflict TryClassifyConflict(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(destinationPath))
            return ImportFileConflict.Move;

        return ImportDestinationPolicy.ClassifyFileConflict(
            destinationExists: true,
            ImportDuplicateDetector.AreFilesIdentical(sourcePath, destinationPath, cancellationToken));
    }

    private static void EnsureDestinationDirectory(
        string destinationPath,
        ISet<string> createdDirectories)
    {
        var directory = Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("The destination path has no directory.", nameof(destinationPath));
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            createdDirectories.Add(Path.GetFullPath(directory));
        }
    }

    private static bool TryGetRollbackDirectory(
        ImportItemTransactionReceipt receipt,
        string? recoveryDirectory,
        out string rollbackDirectory)
    {
        var sourceDirectory = Path.GetDirectoryName(receipt.SourceFilePath);
        if (!string.IsNullOrWhiteSpace(sourceDirectory) && Directory.Exists(sourceDirectory))
        {
            rollbackDirectory = sourceDirectory;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(recoveryDirectory) && Directory.Exists(recoveryDirectory))
        {
            rollbackDirectory = recoveryDirectory;
            return true;
        }

        rollbackDirectory = string.Empty;
        return false;
    }

    private static string MoveWithRenameRetry(string sourcePath, string idealDestinationPath)
    {
        var directory = Path.GetDirectoryName(idealDestinationPath)!;
        var name = Path.GetFileNameWithoutExtension(idealDestinationPath);
        var extension = Path.GetExtension(idealDestinationPath);

        for (var attempt = 0; attempt <= MaxRenameAttempts; attempt++)
        {
            var destinationPath = attempt == 0
                ? idealDestinationPath
                : Path.Combine(directory, $"{name}_{attempt}{extension}");
            try
            {
                File.Move(sourcePath, destinationPath);
                return destinationPath;
            }
            catch (IOException exception) when (IsFileExists(exception))
            {
                continue;
            }
        }

        throw new AtomicRenameExhaustedException();
    }

    private static bool TryCompensateMove(string sourcePath, string destinationPath)
    {
        try
        {
            File.Move(sourcePath, destinationPath);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static TransactionFailureType ClassifyMoveFailure(Exception exception)
        => exception switch
        {
            UnauthorizedAccessException => TransactionFailureType.FileMoveAccessDenied,
            IOException ioException when IsHResult(ioException, 112) => TransactionFailureType.TargetVolumeFull,
            IOException ioException when IsHResult(ioException, 2) || IsHResult(ioException, 3) => TransactionFailureType.SourceFileNotFound,
            _ => TransactionFailureType.FileMoveAccessDenied,
        };

    private static bool IsFileExists(IOException exception)
        => IsHResult(exception, 80) || IsHResult(exception, 183);

    private static bool IsHResult(IOException exception, int win32Error)
        => (exception.HResult & 0xFFFF) == win32Error;

    private static void CleanupEmptyDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories.OrderByDescending(static path => path.Length))
        {
            try
            {
                if (Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception)
            {
                // Empty-directory cleanup is post-commit housekeeping only.
            }
        }
    }

    private static void DeleteCreatedEmptyDirectories(IEnumerable<string> directories)
        => CleanupEmptyDirectories(directories);

    private static void Report(
        IProgress<ImportExecutionProgressSnapshot>? progress,
        ImportExecutionPhase phase,
        int completed,
        int total,
        int success,
        int failed,
        int manualRecoveryRequired,
        int warningCount,
        ImportItemTransactionReceipt receipt,
        ImportExecutionWarningType warning = ImportExecutionWarningType.None)
        => progress?.Report(new ImportExecutionProgressSnapshot(
            phase,
            completed,
            total,
            success,
            failed,
            manualRecoveryRequired,
            warningCount,
            receipt.LastDurableState,
            receipt.FailureType,
            warning));

    private sealed class DestinationContentMismatchException : Exception;

    private sealed class AtomicRenameExhaustedException : Exception;

    private sealed record RollbackResult(
        IReadOnlyList<ImportItemTransactionReceipt> Receipts,
        int SuccessCount,
        int FailedCount,
        int ManualRecoveryRequiredCount);

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

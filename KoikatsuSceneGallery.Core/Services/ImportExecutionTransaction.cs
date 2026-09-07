using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// The externally observable state of one item in an import transaction.
/// </summary>
internal enum TransactionItemState
{
    Prepared,
    FileMoved,
    SidecarCommitted,
    DuplicateSourceDeleted,
    Failed,
}

/// <summary>
/// Categorizes a failed import operation without discarding its durable state.
/// </summary>
internal enum TransactionFailureType
{
    None,
    Cancelled,
    SourceFileNotFound,
    TargetVolumeFull,
    FileMoveAccessDenied,
    DestinationContentMismatch,
    AtomicRenameExhausted,
    SidecarWriteFault,
}

internal enum ImportExecutionPhase
{
    Executing,
    RollingBack,
    Completed,
}

internal enum ImportExecutionWarningType
{
    None,
    DuplicateSourceCleanupFailed,
}

/// <summary>
/// The recovery operation that the execution pipeline must perform for one item.
/// </summary>
internal enum TransactionRollbackAction
{
    None,
    MoveFileBack,
    MoveFileBackThenUnbindSidecar,
    ManualRecoveryRequired,
}

/// <summary>
/// Immutable input for one disk operation. A null document represents a
/// manually classified card that deliberately has no sidecar association.
/// </summary>
internal sealed record ImportItemPlan(
    string SourceFilePath,
    string IdealDestinationPath,
    string? AuthorDirectoryPath,
    PostMetadataDocument? Document);

/// <summary>
/// UI-neutral execution progress. During <see cref="ImportExecutionPhase.RollingBack"/>,
/// completed and success counts describe rollback work rather than forward commits.
/// </summary>
internal sealed record ImportExecutionProgressSnapshot(
    ImportExecutionPhase Phase,
    int CompletedCount,
    int TotalCount,
    int SuccessCount,
    int FailedCount,
    int ManualRecoveryRequiredCount,
    int WarningCount,
    TransactionItemState LastItemState,
    TransactionFailureType LastFailureType,
    ImportExecutionWarningType LastWarningType);

internal sealed record ImportExecutionWarning(
    ImportExecutionWarningType Type,
    string SourceFilePath,
    string Message);

/// <summary>
/// A durable, item-level record for import execution and undo.
///
/// <para>
/// <see cref="LastDurableState"/> deliberately remains separate from
/// <see cref="FinalState"/>. A failure is an outcome, whereas rollback must
/// know the last write that actually reached disk.
/// </para>
/// </summary>
internal sealed record ImportItemTransactionReceipt(
    string SourceFilePath,
    string TargetFilePath,
    string? ProviderId,
    string? ArtworkId,
    string? AuthorDirectoryPath,
    bool RequiresSidecarAssociation,
    TransactionItemState FinalState,
    TransactionItemState LastDurableState,
    TransactionFailureType FailureType,
    SidecarWriteReceipt? SidecarReceipt)
{
    /// <summary>
    /// True only when rollback could not safely restore this item and the
    /// destination file must be recovered by an explicit user decision.
    /// </summary>
    public bool RequiresManualRecovery { get; init; }
}

/// <summary>
/// The receipt for one complete import attempt. It contains successful,
/// partially completed, and failed items so a later undo can use the same
/// evidence as immediate rollback.
/// </summary>
internal sealed record ImportExecutionReceipt(
    Guid TransactionId,
    DateTimeOffset ExecutedAt,
    IReadOnlyList<ImportItemTransactionReceipt> ItemReceipts,
    bool IsFullySuccessful)
{
    public IReadOnlyList<ImportExecutionWarning> Warnings { get; init; } = [];
}

/// <summary>
/// A side-effect-free recovery plan. The execution layer will later map this
/// plan to file moves and sidecar calls.
/// </summary>
internal sealed record ImportTransactionRollbackPlan(
    TransactionRollbackAction Action,
    bool DeleteSidecarIfEmpty);

internal static class ImportTransactionRollbackPlanner
{
    public static ImportTransactionRollbackPlan CreatePlan(
        ImportItemTransactionReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        return receipt.LastDurableState switch
        {
            TransactionItemState.Prepared or TransactionItemState.DuplicateSourceDeleted =>
                new(TransactionRollbackAction.None, DeleteSidecarIfEmpty: false),

            TransactionItemState.FileMoved =>
                new(TransactionRollbackAction.MoveFileBack, DeleteSidecarIfEmpty: false),

            TransactionItemState.SidecarCommitted when !receipt.RequiresSidecarAssociation =>
                new(TransactionRollbackAction.MoveFileBack, DeleteSidecarIfEmpty: false),

            TransactionItemState.SidecarCommitted => CreateCommittedSidecarPlan(receipt),

            _ =>
                new(TransactionRollbackAction.ManualRecoveryRequired, DeleteSidecarIfEmpty: false),
        };
    }

    private static ImportTransactionRollbackPlan CreateCommittedSidecarPlan(
        ImportItemTransactionReceipt receipt)
    {
        var sidecarReceipt = receipt.SidecarReceipt;
        var targetFileName = Path.GetFileName(receipt.TargetFilePath);
        var canUnbind = sidecarReceipt is not null
            && string.Equals(sidecarReceipt.ProviderId, receipt.ProviderId, StringComparison.Ordinal)
            && string.Equals(sidecarReceipt.ArtworkId, receipt.ArtworkId, StringComparison.Ordinal)
            && sidecarReceipt.NewlyAddedFileNames.Contains(
                targetFileName,
                StringComparer.OrdinalIgnoreCase);

        return canUnbind
            ? new(
                TransactionRollbackAction.MoveFileBackThenUnbindSidecar,
                sidecarReceipt!.SidecarCreatedByThisImport)
            : new(TransactionRollbackAction.ManualRecoveryRequired, DeleteSidecarIfEmpty: false);
    }
}

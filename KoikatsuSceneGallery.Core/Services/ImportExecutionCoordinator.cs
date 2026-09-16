namespace KoikatsuSceneGallery.Services;

internal sealed record ImportExecutionResult(
    ImportExecutionReceipt Receipt,
    int SafelyRolledBackCount,
    int FailedCount,
    int WarningCount,
    int ManualRecoveryCount,
    IReadOnlyList<string> CommittedPaths,
    IReadOnlyList<string> DuplicatePaths)
{
    internal static ImportExecutionResult FromReceipt(ImportExecutionReceipt receipt) => new(
        receipt,
        receipt.ItemReceipts.Count(item => item.FinalState == TransactionItemState.Prepared
            && item.FailureType != TransactionFailureType.None && !item.RequiresManualRecovery),
        receipt.ItemReceipts.Count(item => item.FailureType != TransactionFailureType.None),
        receipt.Warnings.Count,
        receipt.ItemReceipts.Count(item => item.RequiresManualRecovery),
        receipt.ItemReceipts.Where(item => item.FinalState == TransactionItemState.SidecarCommitted)
            .Select(item => item.TargetFilePath).ToArray(),
        // Sources the library already had a copy of, left where the user keeps
        // them. Nothing else reports these: they are neither committed nor
        // failed.
        receipt.ItemReceipts
            .Where(item => item.FinalState == TransactionItemState.DuplicateSourceKept)
            .Select(item => item.SourceFilePath).ToArray());
}

/// <summary>
/// Owns cancellation until the executor finishes (including rollback).
/// The dispatcher schedules callbacks on the UI thread; it must not block waiting for that thread.
/// Executor failures propagate to the caller. Cancellation never abandons execution.
/// </summary>
internal sealed class ImportExecutionCoordinator(
    Func<IReadOnlyList<ImportItemPlan>, IProgress<ImportExecutionProgressSnapshot>, CancellationToken,
        Task<ImportExecutionReceipt>> execute,
    Action<Action> dispatch)
{
    private readonly object _gate = new();
    private CancellationTokenSource? _active;

    public async Task<ImportExecutionResult> ExecuteAsync(
        IReadOnlyList<ImportItemPlan> plans, Action<ImportExecutionProgressSnapshot> onProgress)
    {
        CancellationTokenSource owner;
        lock (_gate)
        {
            if (_active is not null)
                throw new InvalidOperationException("An import transaction is already running.");
            owner = _active = new CancellationTokenSource();
        }

        try
        {
            var progress = new CallbackProgress(snapshot => dispatch(() =>
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_active, owner)) onProgress(snapshot);
                }
            }));
            return ImportExecutionResult.FromReceipt(await execute(plans, progress, owner.Token));
        }
        finally
        {
            lock (_gate)
            {
                _active = null;
                owner.Dispose();
            }
        }
    }

    public void Cancel()
    {
        lock (_gate) _active?.Cancel();
    }

    private sealed class CallbackProgress(Action<ImportExecutionProgressSnapshot> report)
        : IProgress<ImportExecutionProgressSnapshot>
    {
        public void Report(ImportExecutionProgressSnapshot value) => report(value);
    }
}

using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportExecutionCoordinatorTests
{
    private static ImportExecutionReceipt Receipt(bool success = true) =>
        new(Guid.NewGuid(), DateTimeOffset.UtcNow, [], success);

    private static ImportExecutionProgressSnapshot Snapshot => new(
        ImportExecutionPhase.Executing, 1, 2, 1, 0, 0, 0,
        TransactionItemState.SidecarCommitted, TransactionFailureType.None, ImportExecutionWarningType.None);

    [Fact]
    public async Task CancelWaitsForExecutorAndRejectsConcurrentExecution()
    {
        var completion = new TaskCompletionSource<ImportExecutionReceipt>();
        CancellationToken token = default;
        var coordinator = new ImportExecutionCoordinator((plans, progress, ct) =>
        {
            token = ct;
            return completion.Task;
        }, action => action());

        var running = coordinator.ExecuteAsync([], _ => { });
        coordinator.Cancel();
        Assert.True(token.IsCancellationRequested);
        Assert.False(running.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ExecuteAsync([], _ => { }));
        var receipt = Receipt(false);
        completion.SetResult(receipt);
        Assert.Same(receipt, (await running).Receipt);
        coordinator.Cancel(); // No disposed CTS remains exposed.
    }

    [Fact]
    public async Task DelayedProgressCannotOverwriteCompletionOrNextTransaction()
    {
        var queue = new Queue<Action>();
        var completions = new Queue<TaskCompletionSource<ImportExecutionReceipt>>();
        var first = new TaskCompletionSource<ImportExecutionReceipt>();
        var second = new TaskCompletionSource<ImportExecutionReceipt>();
        completions.Enqueue(first);
        completions.Enqueue(second);
        IProgress<ImportExecutionProgressSnapshot>? producer = null;
        var coordinator = new ImportExecutionCoordinator((plans, progress, ct) =>
        {
            producer = progress;
            return completions.Dequeue().Task;
        }, queue.Enqueue);
        var updates = 0;
        var run1 = coordinator.ExecuteAsync([], _ => updates++);
        var oldProducer = producer!;
        oldProducer.Report(Snapshot);
        queue.Dequeue()();
        Assert.Equal(1, updates);
        oldProducer.Report(Snapshot);
        first.SetResult(Receipt());
        await run1;
        queue.Dequeue()();
        Assert.Equal(1, updates);

        oldProducer.Report(Snapshot);
        var run2 = coordinator.ExecuteAsync([], _ => updates++);
        queue.Dequeue()();
        Assert.Equal(1, updates);
        producer!.Report(Snapshot);
        queue.Dequeue()();
        Assert.Equal(2, updates);
        second.SetResult(Receipt());
        await run2;
    }

    [Fact]
    public async Task FailureInvalidatesProgressAndAllowsRetry()
    {
        var queue = new Queue<Action>();
        var attempts = 0;
        var coordinator = new ImportExecutionCoordinator((plans, progress, ct) =>
        {
            progress.Report(Snapshot);
            if (++attempts == 1) throw new IOException("Expected failure");
            return Task.FromResult(Receipt());
        }, queue.Enqueue);
        await Assert.ThrowsAsync<IOException>(() => coordinator.ExecuteAsync([], _ => Assert.Fail("Stale progress")));
        queue.Dequeue()();
        Assert.True((await coordinator.ExecuteAsync([], _ => { })).Receipt.IsFullySuccessful);
    }

    [Fact]
    public void ResultPreservesExistingReceiptClassification()
    {
        ImportItemTransactionReceipt Item(TransactionItemState state, TransactionFailureType failure,
            bool manual = false) => new("source", state.ToString(), null, null, null, false,
                state, state, failure, null) { RequiresManualRecovery = manual };
        var receipt = new ImportExecutionReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow,
            [
                Item(TransactionItemState.Prepared, TransactionFailureType.Cancelled),
                Item(TransactionItemState.Prepared, TransactionFailureType.None),
                Item(TransactionItemState.Failed, TransactionFailureType.SidecarWriteFault, true),
                Item(TransactionItemState.SidecarCommitted, TransactionFailureType.None),
                Item(TransactionItemState.DuplicateSourceDeleted, TransactionFailureType.None)
            ], false)
        {
            Warnings = [new(ImportExecutionWarningType.DuplicateSourceCleanupFailed, "source", "warning")]
        };
        var result = ImportExecutionResult.FromReceipt(receipt);
        Assert.Same(receipt, result.Receipt);
        Assert.Equal(1, result.SafelyRolledBackCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.ManualRecoveryCount);
        Assert.Equal(["SidecarCommitted"], result.CommittedPaths);
    }

    [Fact]
    public async Task SuccessForwardsPlansAndProducesCommittedPaths()
    {
        ImportItemPlan[] plans = [new("source", "target", null, null)];
        var receipt = new ImportExecutionReceipt(Guid.NewGuid(), DateTimeOffset.UtcNow,
            [new("source", "target", null, null, null, false,
                TransactionItemState.SidecarCommitted, TransactionItemState.SidecarCommitted,
                TransactionFailureType.None, null)], true);
        var coordinator = new ImportExecutionCoordinator((input, progress, ct) =>
        {
            Assert.Same(plans, input);
            Assert.False(ct.IsCancellationRequested);
            return Task.FromResult(receipt);
        }, action => action());
        var result = await coordinator.ExecuteAsync(plans, _ => { });
        Assert.True(result.Receipt.IsFullySuccessful);
        Assert.Equal(["target"], result.CommittedPaths);
        Assert.Equal(0, result.FailedCount);
    }
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportExecutionTransactionTests
{
    [Theory]
    [InlineData((int)TransactionFailureType.Cancelled)]
    [InlineData((int)TransactionFailureType.SourceFileNotFound)]
    [InlineData((int)TransactionFailureType.TargetVolumeFull)]
    [InlineData((int)TransactionFailureType.FileMoveAccessDenied)]
    [InlineData((int)TransactionFailureType.AtomicRenameExhausted)]
    public void PreparedItem_DoesNotRequireRollback(int failureTypeValue)
    {
        var plan = ImportTransactionRollbackPlanner.CreatePlan(Receipt(
            finalState: TransactionItemState.Failed,
            lastDurableState: TransactionItemState.Prepared,
            failureType: (TransactionFailureType)failureTypeValue));

        Assert.Equal(TransactionRollbackAction.None, plan.Action);
        Assert.False(plan.DeleteSidecarIfEmpty);
    }

    [Fact]
    public void FileMovedItemAfterSidecarWriteFault_OnlyMovesTheFileBack()
    {
        var plan = ImportTransactionRollbackPlanner.CreatePlan(Receipt(
            finalState: TransactionItemState.Failed,
            lastDurableState: TransactionItemState.FileMoved,
            failureType: TransactionFailureType.SidecarWriteFault));

        Assert.Equal(TransactionRollbackAction.MoveFileBack, plan.Action);
        Assert.False(plan.DeleteSidecarIfEmpty);
    }

    [Fact]
    public void ManualCardCommit_OnlyMovesTheFileBack()
    {
        var plan = ImportTransactionRollbackPlanner.CreatePlan(Receipt(
            finalState: TransactionItemState.SidecarCommitted,
            lastDurableState: TransactionItemState.SidecarCommitted,
            failureType: TransactionFailureType.None,
            requiresSidecarAssociation: false));

        Assert.Equal(TransactionRollbackAction.MoveFileBack, plan.Action);
    }

    [Fact]
    public void SidecarCommittedItem_MovesBackThenUnbinds_AndDeletesNewSidecarWhenEmpty()
    {
        var receipt = Receipt(
            finalState: TransactionItemState.SidecarCommitted,
            lastDurableState: TransactionItemState.SidecarCommitted,
            failureType: TransactionFailureType.None,
            sidecarReceipt: new SidecarWriteReceipt(
                Path.Combine("library", "author", ".scenegallery", "fetched_data", "pixiv_123.json"),
                "pixiv",
                "123",
                ["card.png"],
                SidecarCreatedByThisImport: true,
                WasWritten: true));

        var plan = ImportTransactionRollbackPlanner.CreatePlan(receipt);

        Assert.Equal(TransactionRollbackAction.MoveFileBackThenUnbindSidecar, plan.Action);
        Assert.True(plan.DeleteSidecarIfEmpty);
    }

    [Fact]
    public void SidecarCommittedItem_PreservesExistingSidecarWhenUndoingItsLastFileName()
    {
        var plan = ImportTransactionRollbackPlanner.CreatePlan(Receipt(
            finalState: TransactionItemState.SidecarCommitted,
            lastDurableState: TransactionItemState.SidecarCommitted,
            failureType: TransactionFailureType.None,
            sidecarReceipt: new SidecarWriteReceipt(
                Path.Combine("library", "author", ".scenegallery", "fetched_data", "pixiv_123.json"),
                "pixiv",
                "123",
                ["card.png"],
                SidecarCreatedByThisImport: false,
                WasWritten: true)));

        Assert.Equal(TransactionRollbackAction.MoveFileBackThenUnbindSidecar, plan.Action);
        Assert.False(plan.DeleteSidecarIfEmpty);
    }

    [Fact]
    public void CommittedStateWithoutProofOfSidecarMutation_RequiresManualRecovery()
    {
        var plan = ImportTransactionRollbackPlanner.CreatePlan(Receipt(
            finalState: TransactionItemState.Failed,
            lastDurableState: TransactionItemState.SidecarCommitted,
            failureType: TransactionFailureType.SidecarWriteFault));

        Assert.Equal(TransactionRollbackAction.ManualRecoveryRequired, plan.Action);
        Assert.False(plan.DeleteSidecarIfEmpty);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_SuccessPath_MovesFileCommitsSidecarAndReportsProgress()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourcePath = source.Write("source.png", "source"u8);
        var targetPath = Path.Combine(destination.Path, "author", "destination.png");
        var progress = new ProgressCollector();

        var receipt = await executor.ExecuteTransactionAsync(
            [CreatePlan(sourcePath, targetPath, Path.GetDirectoryName(targetPath)!, "pixiv", "99999", "Artwork")],
            progress);

        Assert.True(receipt.IsFullySuccessful);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(targetPath));
        var saved = store.Read(Path.GetDirectoryName(targetPath)!, "pixiv", "99999");
        Assert.NotNull(saved);
        Assert.Contains("destination.png", saved.LocalFileNames);
        Assert.Contains(progress.Snapshots, snapshot =>
            snapshot.Phase == ImportExecutionPhase.Executing
            && snapshot.SuccessCount == 1
            && snapshot.LastItemState == TransactionItemState.SidecarCommitted);
        Assert.Equal(ImportExecutionPhase.Completed, progress.Snapshots[^1].Phase);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_SidecarValidationFault_RollsBackFilesAndSidecar()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourceA = source.Write("one.png", "one"u8);
        var sourceB = source.Write("two.png", "two"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        var targetA = Path.Combine(authorDirectory, "one.png");
        var targetB = Path.Combine(authorDirectory, "two.png");
        var invalidDocument = CreateDocument("Broken", "pixiv", "22222") with { ProviderId = "" };
        var progress = new ProgressCollector();

        var receipt = await executor.ExecuteTransactionAsync(
            [
                CreatePlan(sourceA, targetA, authorDirectory, "pixiv", "11111", "First"),
                new ImportItemPlan(sourceB, targetB, authorDirectory, invalidDocument),
            ],
            progress);

        Assert.False(receipt.IsFullySuccessful);
        Assert.True(File.Exists(sourceA));
        Assert.True(File.Exists(sourceB));
        Assert.False(File.Exists(targetA));
        Assert.False(File.Exists(targetB));
        Assert.False(File.Exists(store.GetSidecarPath(authorDirectory, "pixiv", "11111")));
        Assert.Contains(progress.Snapshots, snapshot => snapshot.Phase == ImportExecutionPhase.RollingBack);
        Assert.Equal(TransactionItemState.Prepared, receipt.ItemReceipts[0].FinalState);
        Assert.Equal(TransactionItemState.Prepared, receipt.ItemReceipts[1].FinalState);
        Assert.Equal(TransactionFailureType.SidecarWriteFault, receipt.ItemReceipts[1].FailureType);
        Assert.Contains(progress.Snapshots, snapshot =>
            snapshot.Phase == ImportExecutionPhase.Executing
            && snapshot.LastFailureType == TransactionFailureType.SidecarWriteFault);
    }

    [Fact]
    public async Task RollbackTransactionAsync_SourceCollision_RenamesInsteadOfOverwriting()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourcePath = source.Write("card.png", "original"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        var targetPath = Path.Combine(authorDirectory, "card.png");

        var receipt = await executor.ExecuteTransactionAsync(
            [CreatePlan(sourcePath, targetPath, authorDirectory, "pixiv", "88888", "Title")]);
        Assert.True(receipt.IsFullySuccessful);

        Directory.CreateDirectory(source.Path);
        await File.WriteAllTextAsync(sourcePath, "user replacement");
        var rollback = await executor.RollbackTransactionAsync(receipt, null);
        var renamedPath = Path.Combine(source.Path, "card_1.png");

        Assert.True(rollback.IsFullySuccessful);
        Assert.Equal("user replacement", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal("original", await File.ReadAllTextAsync(renamedPath));
        Assert.False(File.Exists(targetPath));
        Assert.False(File.Exists(store.GetSidecarPath(authorDirectory, "pixiv", "88888")));
        Assert.Equal(TransactionItemState.Prepared, rollback.ItemReceipts[0].FinalState);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_DestinationMismatch_RollsBackEarlierCommittedItem()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourceA = source.Write("one.png", "one"u8);
        var sourceB = source.Write("two.png", "two"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        var targetA = Path.Combine(authorDirectory, "one.png");
        var targetB = Path.Combine(authorDirectory, "two.png");
        Directory.CreateDirectory(authorDirectory);
        await File.WriteAllTextAsync(targetB, "different");

        var receipt = await executor.ExecuteTransactionAsync(
            [
                CreatePlan(sourceA, targetA, authorDirectory, "pixiv", "11111", "First"),
                CreatePlan(sourceB, targetB, authorDirectory, "pixiv", "22222", "Second"),
            ]);

        Assert.False(receipt.IsFullySuccessful);
        Assert.True(File.Exists(sourceA));
        Assert.True(File.Exists(sourceB));
        Assert.False(File.Exists(targetA));
        Assert.Equal("different", await File.ReadAllTextAsync(targetB));
        Assert.False(File.Exists(store.GetSidecarPath(authorDirectory, "pixiv", "11111")));
        Assert.Equal(TransactionFailureType.DestinationContentMismatch, receipt.ItemReceipts[1].FailureType);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_DuplicateSourceIsNotDeletedWhenLaterItemFails()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var duplicateSource = source.Write("duplicate.png", "same"u8);
        var invalidSource = source.Write("invalid.png", "invalid"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        Directory.CreateDirectory(authorDirectory);
        var duplicateTarget = Path.Combine(authorDirectory, "duplicate.png");
        await File.WriteAllTextAsync(duplicateTarget, "same");

        var receipt = await executor.ExecuteTransactionAsync(
            [
                CreatePlan(duplicateSource, duplicateTarget, authorDirectory, "pixiv", "33333", "Duplicate"),
                new ImportItemPlan(
                    invalidSource,
                    Path.Combine(authorDirectory, "invalid.png"),
                    authorDirectory,
                    CreateDocument("Broken", "pixiv", "44444") with { ProviderId = "" }),
            ]);

        Assert.False(receipt.IsFullySuccessful);
        Assert.True(File.Exists(duplicateSource));
        Assert.True(File.Exists(duplicateTarget));
    }

    [Fact]
    public async Task ExecuteTransactionAsync_DuplicateSourceIsDeletedOnlyAfterCommit()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourcePath = source.Write("duplicate.png", "same"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        Directory.CreateDirectory(authorDirectory);
        var targetPath = Path.Combine(authorDirectory, "duplicate.png");
        await File.WriteAllTextAsync(targetPath, "same");

        var receipt = await executor.ExecuteTransactionAsync(
            [CreatePlan(sourcePath, targetPath, authorDirectory, "pixiv", "55555", "Duplicate")]);

        Assert.True(receipt.IsFullySuccessful);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(targetPath));
        Assert.Equal(TransactionItemState.DuplicateSourceDeleted, receipt.ItemReceipts[0].FinalState);
        Assert.Empty(receipt.Warnings);
    }

    [Fact]
    public async Task ExecuteTransactionAsync_SourceMissingBeforeMove_DoesNotRequireManualRecovery()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var executor = new ImportTransactionExecutor(new PostMetadataStore());
        var missingSourcePath = Path.Combine(source.Path, "missing.png");
        var targetPath = Path.Combine(destination.Path, "author", "missing.png");

        var receipt = await executor.ExecuteTransactionAsync(
            [new ImportItemPlan(missingSourcePath, targetPath, null, null)]);

        Assert.False(receipt.IsFullySuccessful);
        Assert.False(receipt.ItemReceipts[0].RequiresManualRecovery);
        Assert.Equal(TransactionFailureType.SourceFileNotFound, receipt.ItemReceipts[0].FailureType);
    }

    [Fact]
    public async Task RollbackTransactionAsync_MissingSourceDirectory_RequiresManualRecovery()
    {
        using var source = new TestDirectory();
        using var destination = new TestDirectory();
        var store = new PostMetadataStore();
        var executor = new ImportTransactionExecutor(store);
        var sourcePath = source.Write("card.png", "original"u8);
        var authorDirectory = Path.Combine(destination.Path, "author");
        var targetPath = Path.Combine(authorDirectory, "card.png");

        var receipt = await executor.ExecuteTransactionAsync(
            [CreatePlan(sourcePath, targetPath, authorDirectory, "pixiv", "77777", "Title")]);
        Assert.True(receipt.IsFullySuccessful);
        Assert.False(Directory.Exists(source.Path));

        var rollback = await executor.RollbackTransactionAsync(receipt, null);

        Assert.False(rollback.IsFullySuccessful);
        Assert.True(rollback.ItemReceipts[0].RequiresManualRecovery);
        Assert.True(File.Exists(targetPath));
        Assert.True(File.Exists(store.GetSidecarPath(authorDirectory, "pixiv", "77777")));
    }

    private static PostMetadataDocument CreateDocument(string title, string providerId, string artworkId)
        => new(
            PostMetadataDocument.CurrentSchemaVersion,
            providerId,
            artworkId,
            "Test Author",
            "test-author",
            title,
            "Test Description",
            Rating: 0,
            Tags: [new PostMetadataTag("TestTag", null)],
            FetchedAt: new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.FromHours(8)))
        {
            LocalFileNames = [],
        };

    private static ImportItemPlan CreatePlan(
        string sourcePath,
        string idealDestinationPath,
        string authorDirectory,
        string providerId,
        string artworkId,
        string title)
        => new(
            sourcePath,
            idealDestinationPath,
            authorDirectory,
            CreateDocument(title, providerId, artworkId));

    private static ImportItemTransactionReceipt Receipt(
        TransactionItemState finalState,
        TransactionItemState lastDurableState,
        TransactionFailureType failureType,
        bool requiresSidecarAssociation = true,
        SidecarWriteReceipt? sidecarReceipt = null)
        => new(
            Path.Combine("drop", "card.png"),
            Path.Combine("library", "author", "card.png"),
            "pixiv",
            "123",
            Path.Combine("library", "author"),
            requiresSidecarAssociation,
            finalState,
            lastDurableState,
            failureType,
            sidecarReceipt);

    private sealed class ProgressCollector : IProgress<ImportExecutionProgressSnapshot>
    {
        public List<ImportExecutionProgressSnapshot> Snapshots { get; } = [];

        public void Report(ImportExecutionProgressSnapshot value) => Snapshots.Add(value);
    }
}

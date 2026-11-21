using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ImportFailureSummaryTests
{
    private static ImportItemTransactionReceipt Receipt(string name, TransactionFailureType failure)
        => new(
            SourceFilePath: "D:/in/" + name,
            TargetFilePath: "E:/lib/" + name,
            ProviderId: "pixiv",
            ArtworkId: "1",
            AuthorDirectoryPath: "E:/lib",
            RequiresSidecarAssociation: true,
            FinalState: failure == TransactionFailureType.None ? TransactionItemState.SidecarCommitted : TransactionItemState.Failed,
            LastDurableState: TransactionItemState.Prepared,
            FailureType: failure,
            SidecarReceipt: null);

    [Fact]
    public void NothingFailedMeansNothingToReport()
        => Assert.Null(ImportFailureSummary.From([Receipt("a.png", TransactionFailureType.None)]));

    [Fact]
    public void ACancellationIsTheUsersOwnDoingAndIsNotReported()
        => Assert.Null(ImportFailureSummary.From([
            Receipt("a.png", TransactionFailureType.None),
            Receipt("b.png", TransactionFailureType.Cancelled)]));

    [Fact]
    public void TheFirstFailureIsTheCauseAndTheTotalCountsEveryFailure()
    {
        var summary = ImportFailureSummary.From([
            Receipt("ok.png", TransactionFailureType.None),
            Receipt("clash.png", TransactionFailureType.DestinationContentMismatch),
            Receipt("later.png", TransactionFailureType.FileMoveAccessDenied)]);

        Assert.NotNull(summary);
        Assert.Equal("D:/in/clash.png", summary.First.SourceFilePath);
        Assert.Equal(TransactionFailureType.DestinationContentMismatch, summary.First.FailureType);
        Assert.Equal(2, summary.Total);
    }

    [Fact]
    public void ACancellationBeforeARealFailureDoesNotHideIt()
    {
        var summary = ImportFailureSummary.From([
            Receipt("a.png", TransactionFailureType.Cancelled),
            Receipt("b.png", TransactionFailureType.TargetVolumeFull)]);

        Assert.Equal(TransactionFailureType.TargetVolumeFull, summary!.First.FailureType);
        Assert.Equal(1, summary.Total);
    }
}

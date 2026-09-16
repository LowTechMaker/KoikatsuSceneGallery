using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// The message a failed local import shows. Reported as a defect twice over:
/// it never said which card was at fault, and its count was the executor's
/// failed-item count under wording that describes the whole batch.
/// </summary>
public sealed class LocalImportFailureTextTests
{
    /// <summary>
    /// Stands in for the resource loader, echoing back a key that carries no
    /// text of its own so an assertion can see which one was chosen.
    /// </summary>
    private static string Key(string key) => key switch
    {
        "LocalSources_NameSeparator" => "、",
        "LocalSources_ImportFailedCards" => "failed:{0}|{1}|{2}",
        "LocalSources_ImportFailedUnknown" => "unknown:{0}",
        "LocalSources_ImportFailedManual" => "manual:{0}",
        "LocalSources_FailedCardsMore" => "{0}+{1}",
        _ => key,
    };

    private static ImportItemTransactionReceipt Receipt(
        string source,
        TransactionFailureType failure = TransactionFailureType.None,
        bool manualRecovery = false,
        string? target = null)
        => new(
            source,
            target ?? Path.Combine(@"C:\library", Path.GetFileName(source)),
            null,
            null,
            null,
            false,
            failure == TransactionFailureType.None
                ? TransactionItemState.SidecarCommitted
                : TransactionItemState.Failed,
            TransactionItemState.Prepared,
            failure,
            null)
        {
            RequiresManualRecovery = manualRecovery,
        };

    private static string Describe(IReadOnlyList<ImportItemTransactionReceipt> receipts, int staged)
        => LocalImportFailureText.Describe(receipts, staged, Key);

    // The reported defect: the count has to be the batch the sentence talks
    // about, not the one item the executor stopped on.
    [WindowsFact]
    public void TheCountIsTheWholeBatchAndTheOffendingCardIsNamed()
    {
        var message = Describe(
            [
                Receipt(@"D:\drop\a.png"),
                Receipt(@"D:\drop\b.png", TransactionFailureType.DestinationContentMismatch),
            ],
            staged: 56);

        Assert.Equal("failed:b.png|LocalSources_Failure_ContentMismatch|56", message);
    }

    [Fact]
    public void EveryFailureCategoryHasItsOwnReason()
    {
        var keys = Enum.GetValues<TransactionFailureType>()
            .Where(failure => failure != TransactionFailureType.None)
            .Select(LocalImportFailureText.ReasonKey)
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.DoesNotContain("LocalSources_Failure_Unknown", keys);
    }

    [WindowsFact]
    public void SeveralFailedCardsAreAllNamed()
    {
        var message = Describe(
            [
                Receipt(@"D:\drop\a.png", TransactionFailureType.SourceFileNotFound),
                Receipt(@"D:\drop\b.png", TransactionFailureType.SourceFileNotFound),
            ],
            staged: 4);

        Assert.Equal("failed:a.png、b.png|LocalSources_Failure_SourceMissing|4", message);
    }

    [WindowsFact]
    public void ALongListOfNamesIsTruncatedWithACount()
    {
        var message = Describe(
            [.. Enumerable.Range(0, 7).Select(i =>
                Receipt($@"D:\drop\card{i}.png", TransactionFailureType.Cancelled))],
            staged: 7);

        Assert.Equal(
            "failed:card0.png、card1.png、card2.png+4|LocalSources_Failure_Cancelled|7",
            message);
    }

    // A stranded card is neither imported nor back where it was, so it gets a
    // message of its own rather than a count that would be wrong.
    [WindowsFact]
    public void AStrandedCardIsReportedByItsNameInTheLibrary()
    {
        var message = Describe(
            [
                Receipt(
                    @"D:\drop\a.png",
                    TransactionFailureType.FileMoveAccessDenied,
                    manualRecovery: true,
                    target: @"C:\library\friend\a_1.png"),
            ],
            staged: 3);

        Assert.Equal("manual:a_1.png", message);
    }

    // A rollback can succeed with no item marked failed at all; the message
    // still has to be about the batch rather than empty.
    [Fact]
    public void AFailureWithNoIdentifiableItemStillReportsTheBatch()
        => Assert.Equal("unknown:9", Describe([Receipt(@"D:\drop\a.png")], staged: 9));

    [Fact]
    public void AnEmptyReceiptDoesNotThrow()
        => Assert.Equal("unknown:0", Describe([], staged: 0));
}

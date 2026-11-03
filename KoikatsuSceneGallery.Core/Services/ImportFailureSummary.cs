namespace KoikatsuSceneGallery.Services;

/// <summary>
/// What went wrong in an import that was rolled back, for telling the user: the first
/// file that failed and how many failed in all. The executor stops at the first failure,
/// so that file is the cause; the rest, if any, fell with it.
/// </summary>
/// <param name="First">The first failed item, which carries the source, the target and the reason.</param>
/// <param name="Total">How many items failed, counting <paramref name="First"/>.</param>
internal sealed record ImportFailureSummary(ImportItemTransactionReceipt First, int Total)
{
    /// <summary>
    /// Null when nothing failed for a reason the user needs to hear about. A cancellation is
    /// the user's own doing, so it is not a failure to report.
    /// </summary>
    internal static ImportFailureSummary? From(IEnumerable<ImportItemTransactionReceipt> receipts)
    {
        var failed = receipts
            .Where(receipt => receipt.FailureType is not (TransactionFailureType.None or TransactionFailureType.Cancelled))
            .ToList();
        return failed.Count == 0 ? null : new ImportFailureSummary(failed[0], failed.Count);
    }
}

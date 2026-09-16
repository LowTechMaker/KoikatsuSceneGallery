using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Turns a failed import transaction into a sentence that names the cards at
/// fault and says where every card ended up.
/// </summary>
/// <remarks>
/// The message this replaces reported the executor's failed-item count — which
/// is one, because the transaction stops at the first failure — next to the
/// words "left where they were", of which the true number after a rollback is
/// the whole batch. Two different quantities in one sentence, and the file name
/// only ever reached the log.
/// </remarks>
internal static class LocalImportFailureText
{
    /// <summary>How many names a message lists before it starts counting.</summary>
    public const int MaxNamedCards = 3;

    public static string Describe(
        IReadOnlyList<ImportItemTransactionReceipt> receipts,
        int stagedCount,
        Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        ArgumentNullException.ThrowIfNull(getString);

        // Stranded cards first: they are the only case where a card is neither
        // in the library nor where the user left it, so the count is a lie
        // unless it is reported on its own.
        var stranded = receipts
            .Where(receipt => receipt.RequiresManualRecovery)
            .Select(receipt => Path.GetFileName(
                string.IsNullOrEmpty(receipt.TargetFilePath)
                    ? receipt.SourceFilePath
                    : receipt.TargetFilePath))
            .ToArray();
        if (stranded.Length > 0)
        {
            return string.Format(
                getString("LocalSources_ImportFailedManual"),
                NameList(stranded, getString));
        }

        var failed = receipts
            .Where(receipt => receipt.FailureType != TransactionFailureType.None)
            .ToArray();
        if (failed.Length == 0)
        {
            return string.Format(getString("LocalSources_ImportFailedUnknown"), stagedCount);
        }

        return string.Format(
            getString("LocalSources_ImportFailedCards"),
            NameList(failed.Select(receipt => Path.GetFileName(receipt.SourceFilePath)), getString),
            getString(ReasonKey(failed[0].FailureType)),
            stagedCount);
    }

    /// <summary>The reason a user can act on, per failure category.</summary>
    public static string ReasonKey(TransactionFailureType failure) => failure switch
    {
        TransactionFailureType.DestinationContentMismatch => "LocalSources_Failure_ContentMismatch",
        TransactionFailureType.SourceFileNotFound => "LocalSources_Failure_SourceMissing",
        TransactionFailureType.Cancelled => "LocalSources_Failure_Cancelled",
        TransactionFailureType.TargetVolumeFull => "LocalSources_Failure_DiskFull",
        TransactionFailureType.FileMoveAccessDenied => "LocalSources_Failure_AccessDenied",
        TransactionFailureType.AtomicRenameExhausted => "LocalSources_Failure_NameExhausted",
        TransactionFailureType.SidecarWriteFault => "LocalSources_Failure_Sidecar",
        _ => "LocalSources_Failure_Unknown",
    };

    private static string NameList(IEnumerable<string> names, Func<string, string> getString)
    {
        var all = names.Where(name => !string.IsNullOrEmpty(name)).ToArray();
        var separator = getString("LocalSources_NameSeparator");
        if (all.Length <= MaxNamedCards)
            return string.Join(separator, all);

        return string.Format(
            getString("LocalSources_FailedCardsMore"),
            string.Join(separator, all.Take(MaxNamedCards)),
            all.Length - MaxNamedCards);
    }
}

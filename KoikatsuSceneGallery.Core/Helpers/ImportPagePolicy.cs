namespace KoikatsuSceneGallery.Helpers;

/// <summary>The three steps the import page walks through, in order.</summary>
public enum ImportStep
{
    Add = 0,
    Review = 1,
    Import = 2,
}

/// <summary>The messages the import page can hold at once, most urgent first.</summary>
public enum ImportMessageKind
{
    Failure = 0,
    Rejected = 1,
    Result = 2,
}

/// <summary>
/// Which step the import page is on, and which of its messages to show. The
/// page used to stack three InfoBars; it now shows one at a time, and closing
/// it brings up the next.
/// </summary>
public static class ImportPagePolicy
{
    public static ImportStep CurrentStep(bool hasItems, bool analyzing, bool importing) =>
        importing ? ImportStep.Import
        : hasItems || analyzing ? ImportStep.Review
        : ImportStep.Add;

    /// <summary>
    /// The open messages, most urgent first: a rollback error before the
    /// rejected-files warning before the result of the last import.
    /// </summary>
    public static IReadOnlyList<ImportMessageKind> OpenMessages(bool failure, bool rejected, bool result)
    {
        var open = new List<ImportMessageKind>(3);
        if (failure) open.Add(ImportMessageKind.Failure);
        if (rejected) open.Add(ImportMessageKind.Rejected);
        if (result) open.Add(ImportMessageKind.Result);
        return open;
    }
}

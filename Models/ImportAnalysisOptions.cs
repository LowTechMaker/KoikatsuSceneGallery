namespace KoikatsuSceneGallery.Models;

/// <summary>The local source every card in a batch belongs to.</summary>
public sealed record LocalSourceAssignment(string AuthorId, string AuthorName);

/// <summary>
/// Decides how one import batch is analyzed.
/// </summary>
/// <remarks>
/// A local batch has to be declared before the files are read, not marked
/// afterwards: analysis parses file names for a remote artwork identity and
/// then fetches it, so by the time an item existed to mark, the request would
/// already have gone out. Declaring it up front is what makes "no outbound
/// lookups for local cards" true rather than merely intended.
/// </remarks>
public sealed record ImportAnalysisOptions(LocalSourceAssignment? LocalSource = null)
{
    /// <summary>Ordinary analysis: parse file names, fetch what they identify.</summary>
    public static readonly ImportAnalysisOptions Default = new();

    public bool IsLocalBatch => LocalSource is not null;
}

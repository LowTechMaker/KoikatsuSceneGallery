namespace SceneGallery.PluginSdk;

/// <summary>One file a post link leads to.</summary>
/// <param name="Name">File name as the host service lists it, e.g. "2023_0109_1345_51_630.png".</param>
/// <param name="Size">Size in bytes, when the service reports one.</param>
public sealed record PostLinkFile(string Name, long? Size)
{
    /// <summary>
    /// An address that opens this file in a browser (for a file inside a shared folder, the
    /// folder with the file selected), or null when the service has none. Added after
    /// 1.4.0 as an init property so the two-argument constructor keeps its binary shape.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// The folder the file sits in, relative to the root of the link, with "/" between nested
    /// folders (for example "EP1" or "EP2/Scenes"); null for a file at the root. Authors often
    /// share one folder for a whole series and split it by episode, so the folder is how the
    /// app tells which part of the share a card comes from. Added in 1.7.0.
    /// </summary>
    public string? Folder { get; init; }

    /// <summary>An address that opens <see cref="Folder"/>, when there is a folder and the service has one.</summary>
    public string? FolderUrl { get; init; }
}

/// <summary>What a link found in a post's description leads to.</summary>
/// <param name="Url">The link as inspected, unchanged.</param>
/// <param name="ServiceName">Short name of the service the link points at, e.g. "MEGA".</param>
/// <param name="IsFolder">True when the link leads to a folder, false for a single file.</param>
/// <param name="Files">
/// Every file reachable through the link. A folder is flattened: files in
/// nested folders are included, folders themselves are not.
/// </param>
public sealed record PostLinkInfo(
    string Url,
    string ServiceName,
    bool IsFolder,
    IReadOnlyList<PostLinkFile> Files);

/// <summary>
/// Optional plugin extension that looks inside the download links authors put
/// in a post's description (cloud-storage links, for instance) and reports the
/// file names behind them.
/// <para>
/// The app uses the names to match the cards a user has dropped in against the
/// post, so an implementation must be cheap to refuse: <see cref="CanInspect"/>
/// does no I/O, and <see cref="InspectAsync"/> reads metadata only — it must
/// never download the files themselves.
/// </para>
/// </summary>
public interface IPostLinkInspector : IPlugin
{
    /// <summary>Whether this inspector recognises <paramref name="url"/>. No I/O.</summary>
    bool CanInspect(string url);

    /// <summary>
    /// Lists the files behind <paramref name="url"/>. Returns null when the link
    /// is dead, protected, malformed or the service cannot be reached; a link
    /// that cannot be read is not an error for the caller.
    /// </summary>
    Task<PostLinkInfo?> InspectAsync(string url, CancellationToken ct);
}

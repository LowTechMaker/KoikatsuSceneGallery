namespace SceneGallery.PluginSdk;

/// <summary>
/// A plugin that maps card folder names to author identities and resolves
/// author profiles from an external source.
/// </summary>
public interface IFolderAuthorProvider : IPlugin
{
    /// <summary>Provider identifier, e.g. "pixiv" or "bepisdb".</summary>
    string ProviderId { get; }

    /// <summary>
    /// Extracts an author identity from a single directory name. Called on hot
    /// scan paths: must be pure, fast, and do no I/O. Returns null when the
    /// name carries no recognizable author id.
    /// </summary>
    ParsedAuthor? TryParseFolderName(string folderName);

    /// <summary>
    /// Returns the author's profile, from the plugin's local cache when
    /// available (synchronously-completed task), otherwise over the network.
    /// Network requests are rate-limited internally by the plugin. Returns
    /// null when the author cannot be resolved (deleted account, offline).
    /// </summary>
    Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct);

    /// <summary>Browser-openable profile URL for the author. No I/O.</summary>
    string GetProfileUrl(AuthorKey key);
}

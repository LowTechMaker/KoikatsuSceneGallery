namespace SceneGallery.PluginSdk;

public sealed record PluginUpdateRequest(
    string PluginName,
    string CurrentVersion,
    string UpdateUrl);

public sealed record PluginUpdateResult(
    string Version,
    string? DownloadUrl = null,
    string? Changelog = null);

/// <summary>
/// Optional plugin extension for checking updates from non-JSON release
/// sources. Plugins opt in by adding PluginUpdateUrl assembly metadata.
/// </summary>
public interface IPluginUpdateProvider : IPlugin
{
    bool CanCheckUpdate(PluginUpdateRequest request);

    Task<PluginUpdateResult?> CheckUpdateAsync(PluginUpdateRequest request, CancellationToken ct);
}

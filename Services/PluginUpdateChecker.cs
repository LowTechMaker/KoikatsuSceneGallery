using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

internal sealed class PluginUpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "SceneGallery-PluginUpdater/1.0" } },
    };

    public async Task<Dictionary<string, PluginUpdateInfo>> CheckUpdatesAsync(
        IReadOnlyList<LoadedPluginInfo> plugins, CancellationToken ct)
    {
        var results = new Dictionary<string, PluginUpdateInfo>();

        foreach (var plugin in plugins)
        {
            if (plugin.UpdateUrl is not { Length: > 0 } url)
                continue;

            try
            {
                var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<PluginUpdateInfo>(json);
                if (info?.Version is null)
                    continue;

                if (Version.TryParse(info.Version, out var remote)
                    && Version.TryParse(plugin.Version, out var local)
                    && remote > local)
                {
                    results[plugin.Name] = info;
                }
            }
            catch
            {
                // Best-effort: network failures, bad JSON, etc. are silently ignored.
            }
        }

        return results;
    }
}

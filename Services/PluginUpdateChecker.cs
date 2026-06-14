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
        var candidates = plugins
            .Where(p => p.UpdateUrl is { Length: > 0 })
            .ToList();

        if (candidates.Count == 0)
            return [];

        var tasks = candidates.Select(p => CheckOneAsync(p, ct));
        var pairs = await Task.WhenAll(tasks).ConfigureAwait(false);
        return pairs
            .Where(p => p.Info is not null)
            .ToDictionary(p => p.Name, p => p.Info!);
    }

    private static async Task<(string Name, PluginUpdateInfo? Info)> CheckOneAsync(
        LoadedPluginInfo plugin, CancellationToken ct)
    {
        try
        {
            var json = await Http.GetStringAsync(plugin.UpdateUrl!, ct).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<PluginUpdateInfo>(json);
            if (info?.Version is not null
                && Version.TryParse(info.Version, out var remote)
                && Version.TryParse(plugin.Version, out var local)
                && remote > local)
            {
                return (plugin.Name, info);
            }
        }
        catch { }
        return (plugin.Name, null);
    }
}

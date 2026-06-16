using System.Text.Json;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

internal sealed class PluginUpdateChecker
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10),
        DefaultRequestHeaders = { { "User-Agent", "SceneGallery-PluginUpdater/1.0" } },
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<Dictionary<string, PluginUpdateInfo>> CheckUpdatesAsync(
        IReadOnlyList<LoadedPluginInfo> plugins,
        IReadOnlyList<IPluginUpdateProvider> updateProviders,
        CancellationToken ct)
    {
        var candidates = plugins
            .Where(p => p.UpdateUrl is { Length: > 0 })
            .ToList();

        if (candidates.Count == 0)
            return [];

        var tasks = candidates.Select(p => CheckOneAsync(p, updateProviders, ct));
        var pairs = await Task.WhenAll(tasks).ConfigureAwait(false);
        return pairs
            .Where(p => p.Info is not null)
            .ToDictionary(p => p.Name, p => p.Info!);
    }

    private static async Task<(string Name, PluginUpdateInfo? Info)> CheckOneAsync(
        LoadedPluginInfo plugin,
        IReadOnlyList<IPluginUpdateProvider> updateProviders,
        CancellationToken ct)
    {
        try
        {
            var request = new PluginUpdateRequest(plugin.Name, plugin.Version, plugin.UpdateUrl!);
            foreach (var provider in updateProviders)
            {
                if (!provider.CanCheckUpdate(request))
                    continue;

                var result = await provider.CheckUpdateAsync(request, ct).ConfigureAwait(false);
                if (result is not null && IsNewer(result.Version, plugin.Version))
                {
                    return (plugin.Name, new PluginUpdateInfo
                    {
                        Version = result.Version,
                        DownloadUrl = result.DownloadUrl,
                        Changelog = result.Changelog,
                    });
                }

                return (plugin.Name, null);
            }

            var json = await Http.GetStringAsync(plugin.UpdateUrl!, ct).ConfigureAwait(false);
            var info = JsonSerializer.Deserialize<PluginUpdateInfo>(json, JsonOptions);
            if (info?.Version is not null && IsNewer(info.Version, plugin.Version))
            {
                return (plugin.Name, info);
            }
        }
        catch { }
        return (plugin.Name, null);
    }

    private static bool IsNewer(string remoteVersion, string localVersion)
        => Version.TryParse(NormalizeVersion(remoteVersion), out var remote)
           && Version.TryParse(NormalizeVersion(localVersion), out var local)
           && remote > local;

    private static string NormalizeVersion(string version)
    {
        version = version.Trim();
        if (version.StartsWith('v') || version.StartsWith('V'))
            version = version[1..];
        return version;
    }
}

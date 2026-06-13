using System.Reflection;
using SceneGallery.PluginSdk;

namespace KoikatsuSceneGallery.Services;

public enum PluginStatus
{
    Loaded,
    Failed,
}

public sealed record LoadedPluginInfo(
    string Name,
    string Version,
    PluginStatus Status,
    string? Error,
    string FilePath);

/// <summary>
/// Discovers and loads plugins from the Plugins folder next to the exe. Each
/// plugin gets its own <see cref="PluginLoadContext"/>; every load step is
/// individually guarded so one broken DLL can never stop the app from
/// starting — it just shows up as Failed in the settings page.
/// </summary>
public sealed class PluginService
{
    private readonly List<LoadedPluginInfo> _plugins = [];
    private readonly List<IPlugin> _instances = [];

    /// <summary>Folder scanned for plugins: Plugins\&lt;name&gt;\&lt;name&gt;.dll next to the exe.</summary>
    public static string PluginsDirectory { get; } = ResolvePluginsDirectory();

    private static string ResolvePluginsDirectory()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var primary = Path.Combine(baseDir, "Plugins");
        if (HasPluginAssemblies(primary)) return primary;

        // Packaged dev launches (VS F5 / dotnet run) execute from the AppX
        // layout subfolder of the build output, where the MSIX tooling creates
        // an *empty* Plugins folder; the dev plugin copy lands in the build
        // output itself — so prefer whichever candidate actually has content.
        if (string.Equals(Path.GetFileName(baseDir), "AppX", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(baseDir) is { } parent)
        {
            var sibling = Path.Combine(parent, "Plugins");
            if (HasPluginAssemblies(sibling)) return sibling;
        }
        return primary;
    }

    private static bool HasPluginAssemblies(string pluginsDir)
    {
        try
        {
            return Directory.Exists(pluginsDir)
                && Directory.EnumerateDirectories(pluginsDir)
                    .Any(dir => Directory.EnumerateFiles(dir, "*.dll").Any());
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<LoadedPluginInfo> Plugins => _plugins;

    /// <summary>First loaded author provider, or null when none is installed.</summary>
    public IFolderAuthorProvider? AuthorProvider { get; private set; }

    /// <summary>First loaded import provider, or null when none is installed.</summary>
    public ICardImportProvider? ImportProvider { get; private set; }

    /// <summary>First loaded reverse image search provider, or null when none is installed.</summary>
    public IReverseImageSearchProvider? ReverseImageSearchProvider { get; private set; }

    public void LoadPlugins()
    {
        if (!Directory.Exists(PluginsDirectory)) return;

        foreach (var pluginDir in Directory.EnumerateDirectories(PluginsDirectory))
        {
            var assemblyPath = FindPluginAssembly(pluginDir);
            if (assemblyPath is null) continue;
            LoadPlugin(assemblyPath);
        }
    }

    private static string? FindPluginAssembly(string pluginDir)
    {
        // Convention: Plugins\PixivAuthors\PixivAuthors.dll, with a fallback
        // to a single SceneGallery.Plugin.*.dll so zips extracted with their
        // build-output names also work.
        var byDirName = Path.Combine(pluginDir, Path.GetFileName(pluginDir) + ".dll");
        if (File.Exists(byDirName)) return byDirName;

        var candidates = Directory.GetFiles(pluginDir, "SceneGallery.Plugin.*.dll");
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private void LoadPlugin(string assemblyPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(assemblyPath);
        try
        {
            var context = new PluginLoadContext(assemblyPath);
            var assembly = context.LoadFromAssemblyPath(assemblyPath);

            var pluginTypes = assembly.GetExportedTypes()
                .Where(t => t is { IsAbstract: false, IsClass: true })
                .Where(t => typeof(IPlugin).IsAssignableFrom(t))
                .ToList();

            if (pluginTypes.Count == 0)
            {
                // A type may *look* like IPlugin but fail the cast when a stray
                // copy of the contracts DLL sits in the plugin folder and got
                // loaded into the plugin's context (broken type identity).
                var lookalike = assembly.GetExportedTypes()
                    .Any(t => t.GetInterfaces().Any(i => i.Name == nameof(IPlugin)));
                var error = lookalike
                    ? "Plugin implements IPlugin from a different SceneGallery.PluginSdk.dll — " +
                      "delete the duplicate SceneGallery.PluginSdk.dll from the plugin's folder."
                    : "No IPlugin implementation found in the assembly.";
                _plugins.Add(new LoadedPluginInfo(fileName, "?", PluginStatus.Failed, error, assemblyPath));
                return;
            }

            foreach (var type in pluginTypes)
                InstantiateAndInitialize(type, assemblyPath);
        }
        catch (Exception ex)
        {
            CrashLogPlugin(fileName, ex);
            _plugins.Add(new LoadedPluginInfo(fileName, "?", PluginStatus.Failed, ex.Message, assemblyPath));
        }
    }

    private void InstantiateAndInitialize(Type type, string assemblyPath)
    {
        var name = type.Name;
        try
        {
            var plugin = (IPlugin)Activator.CreateInstance(type)!;
            name = plugin.Name;

            var storageDir = Path.Combine(AppPaths.LocalFolder, "Plugins", Sanitize(plugin.Name));
            Directory.CreateDirectory(storageDir);
            plugin.Initialize(new PluginHost(plugin.Name, storageDir));

            _instances.Add(plugin);

            if (plugin is IFolderAuthorProvider provider)
                AuthorProvider ??= provider;

            if (plugin is ICardImportProvider importProvider)
                ImportProvider ??= importProvider;

            if (plugin is IReverseImageSearchProvider reverseImageSearchProvider)
                ReverseImageSearchProvider ??= reverseImageSearchProvider;

            _plugins.Add(new LoadedPluginInfo(plugin.Name, plugin.Version, PluginStatus.Loaded, null, assemblyPath));
            Log(plugin.Name, $"loaded v{plugin.Version}");
        }
        catch (Exception ex)
        {
            CrashLogPlugin(name, ex);
            _plugins.Add(new LoadedPluginInfo(name, "?", PluginStatus.Failed, ex.Message, assemblyPath));
        }
    }

    public void Shutdown()
    {
        foreach (var plugin in _instances)
        {
            if (plugin is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception ex) { CrashLogPlugin(plugin.Name, ex); }
            }
        }
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void CrashLogPlugin(string pluginName, Exception ex)
        => Helpers.CrashLog.Write($"Plugin:{pluginName}", ex);

    private static readonly object LogGate = new();

    private static void Log(string pluginName, string message)
    {
        // Same best-effort contract as CrashLog: logging must never throw.
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{pluginName}] {message}{Environment.NewLine}";
            lock (LogGate)
                File.AppendAllText(Path.Combine(AppPaths.LocalFolder, "plugins.log"), line);
        }
        catch
        {
        }
    }

    private sealed class PluginHost(string pluginName, string storageDirectory) : IPluginHost
    {
        public string StorageDirectory { get; } = storageDirectory;

        public void Log(string message) => PluginService.Log(pluginName, message);
    }
}

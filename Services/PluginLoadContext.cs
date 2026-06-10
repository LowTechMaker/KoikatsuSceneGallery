using System.Reflection;
using System.Runtime.Loader;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Isolated load context for one plugin, resolving its dependencies from the
/// plugin's own folder via its deps.json. The contracts assembly
/// (SceneGallery.PluginSdk) is deliberately never loaded here: returning null
/// defers it to the default context, so plugin and app share one copy and
/// type identity holds across the boundary.
/// </summary>
internal sealed class PluginLoadContext(string pluginAssemblyPath)
    : AssemblyLoadContext(name: Path.GetFileNameWithoutExtension(pluginAssemblyPath))
{
    private const string ContractsAssemblyName = "SceneGallery.PluginSdk";

    private readonly AssemblyDependencyResolver _resolver = new(pluginAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == ContractsAssemblyName)
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadFromAssemblyPath(path) : null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

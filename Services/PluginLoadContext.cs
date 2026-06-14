using System.Reflection;
using System.Runtime.Loader;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Isolated load context for one plugin. When a deps.json sits next to the
/// assembly, <see cref="AssemblyDependencyResolver"/> resolves the plugin's
/// own dependencies from its folder (folder-based dev layout). When there is
/// no deps.json (single-file deployment), the resolver is skipped and every
/// assembly request falls through to the default context.
/// The contracts assembly (SceneGallery.PluginSdk) is never loaded here:
/// returning null defers it to the default context so plugin and app share
/// one copy and type identity holds across the boundary.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private const string ContractsAssemblyName = "SceneGallery.PluginSdk";

    private readonly AssemblyDependencyResolver? _resolver;

    public PluginLoadContext(string pluginAssemblyPath)
        : base(name: Path.GetFileNameWithoutExtension(pluginAssemblyPath))
    {
        var depsFile = Path.ChangeExtension(pluginAssemblyPath, ".deps.json");
        if (File.Exists(depsFile))
            _resolver = new AssemblyDependencyResolver(pluginAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == ContractsAssemblyName)
            return null;

        var path = _resolver?.ResolveAssemblyToPath(assemblyName);
        if (path is null) return null;

        // COM interop types must live in the default context so their type
        // identity matches the COM objects created by the native runtime.
        if (assemblyName.Name is "Microsoft.Web.WebView2.Core")
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);

        return LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is not null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
    }
}

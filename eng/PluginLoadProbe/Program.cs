using System.Reflection;
using System.Runtime.Loader;
using KoikatsuSceneGallery.Services;
using SceneGallery.PluginSdk;

if (args.Length == 0) throw new ArgumentException("Pass one or more plugin DLL paths; no directory is scanned or modified.");
var expected = new Dictionary<string, (string ProviderId, Type[] Capabilities)>
{
    ["SceneGallery.Plugin.BepisDb"] = ("bepisdb", [typeof(IFolderAuthorProvider), typeof(ICardImportProvider), typeof(IImportDestinationProvider), typeof(ICookieSetupValidator), typeof(IPluginSettingsProvider)]),
    ["SceneGallery.Plugin.PixivAuthors"] = ("pixiv", [typeof(IFolderAuthorProvider), typeof(ICardImportProvider), typeof(IArtworkMetadataRefresher), typeof(IImportDestinationProvider), typeof(IReverseImageSearchProvider), typeof(IPluginSettingsProvider), typeof(ITagDictionaryProvider)]),
    ["SceneGallery.Plugin.Fanbox"] = ("fanbox", [typeof(IFolderAuthorProvider), typeof(ICardImportProvider), typeof(IImportDestinationProvider), typeof(IPluginSettingsProvider)]),
    ["SceneGallery.Plugin.GitHubReleaseUpdates"] = ("", [typeof(IPluginUpdateProvider)])
};
if (typeof(IPlugin).Assembly.GetName().Version != new Version(1, 0, 0, 0))
    throw new InvalidOperationException("SDK assembly identity changed.");
foreach (var argument in args)
{
    var path = Path.GetFullPath(argument);
    if (!File.Exists(path)) throw new FileNotFoundException("Plugin missing", path);
    var context = new PluginLoadContext(path);
    var assembly = context.LoadFromAssemblyPath(path);
    var plugins = assembly.GetExportedTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(t)).ToArray();
    if (plugins.Length != 1) throw new InvalidOperationException($"Expected one SDK-compatible entry point: {path}");
    if (!expected.TryGetValue(assembly.GetName().Name!, out var contract)) throw new InvalidOperationException("Unexpected plugin assembly.");
    var type = plugins[0];
    var capabilities = type.GetInterfaces().Select(t => t.Name).ToArray();
    foreach (var capability in contract.Capabilities)
        if (!capability.IsAssignableFrom(type)) throw new InvalidOperationException($"Missing {capability.Name}: {path}");
    var instance = (IPlugin)Activator.CreateInstance(type)!;
    try
    {
        if (instance is IFolderAuthorProvider author && author.ProviderId != contract.ProviderId) throw new InvalidOperationException("Author provider identity changed.");
        if (instance is ICardImportProvider import && import.ProviderId != contract.ProviderId) throw new InvalidOperationException("Import provider identity changed.");
        if (!ReferenceEquals(type.GetInterfaces().Single(i => i == typeof(IPlugin)).Assembly, typeof(IPlugin).Assembly))
            throw new InvalidOperationException("Duplicate SDK type identity.");
        if (context.Assemblies.Any(a => a.GetName().Name == "SceneGallery.PluginSdk")) throw new InvalidOperationException("SDK loaded into plugin context.");
        Console.WriteLine($"PASS: {instance.Name}; {assembly.GetName().Name}; capabilities={string.Join(',', capabilities)}; SDK=default context");
    }
    finally { (instance as IDisposable)?.Dispose(); }
}
if (AssemblyLoadContext.All.SelectMany(c => c.Assemblies).Count(a => a.GetName().Name == "SceneGallery.PluginSdk") != 1)
    throw new InvalidOperationException("Multiple runtime SDK assemblies found.");
Console.WriteLine("PASS: production PluginLoadContext discovery and SDK identity; no login, network, or browser initialization was performed.");

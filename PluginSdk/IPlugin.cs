namespace SceneGallery.PluginSdk;

/// <summary>
/// Entry point every plugin must implement. Discovered by reflection from
/// assemblies in the app's Plugins folder. A throwing <see cref="Initialize"/>
/// marks the plugin as Failed; the app continues without it.
/// </summary>
public interface IPlugin
{
    string Name { get; }

    string Version { get; }

    void Initialize(IPluginHost host);
}

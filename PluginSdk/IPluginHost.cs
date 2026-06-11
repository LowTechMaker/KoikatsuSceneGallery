namespace SceneGallery.PluginSdk;

/// <summary>
/// Services the app provides to a plugin during <see cref="IPlugin.Initialize"/>.
/// </summary>
public interface IPluginHost
{
    /// <summary>Per-plugin data directory, guaranteed to exist.</summary>
    string StorageDirectory { get; }

    /// <summary>Appends a line to the app's plugin log. Never throws.</summary>
    void Log(string message);
}

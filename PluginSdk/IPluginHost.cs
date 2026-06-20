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

    /// <summary>
    /// Asks the user for a single text value via a modal dialog.
    /// Returns the entered string, or null if the user cancelled.
    /// Hosts that cannot show UI return null immediately.
    /// </summary>
    Task<string?> RequestInputAsync(string title, string message, string? placeholder, CancellationToken ct)
        => Task.FromResult<string?>(null);
}

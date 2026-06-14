namespace SceneGallery.PluginSdk;

public enum PluginSettingValueType
{
    Text,
    Secret,
    Integer,
    Boolean,
}

public sealed record PluginSettingDefinition(
    string Key,
    string Label,
    string? Description,
    PluginSettingValueType ValueType,
    string? DefaultValue = null);

/// <summary>
/// Optional plugin extension for settings that are static user preferences.
/// Real-time setup, authentication, and challenge flows should stay on the
/// feature surface that needs them.
/// </summary>
public interface IPluginSettingsProvider : IPlugin
{
    IReadOnlyList<PluginSettingDefinition> Settings { get; }

    string? GetSettingValue(string key);

    void SetSettingValue(string key, string? value);
}

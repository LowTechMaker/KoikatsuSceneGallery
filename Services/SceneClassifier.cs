using KoikatsuSceneGallery.Models;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Maps a scene's plugin GUID set to derived classification tags. The GUID
/// signatures are deliberately kept here in one place so they can be tuned
/// without touching the parser or UI.
///
/// Signatures confirmed against sample scenes:
///   - Timeline writes scene data under the lowercase "timeline" GUID.
///   - Madevil repack scenes carry plugins prefixed "madevil." (e.g.
///     madevil.kk.SceneEffectsManager, madevil.kk.wtf). HF Patch / other
///     environments have no equivalent positive marker, so "not Madevil" is
///     reported as NonMadevil.
/// </summary>
public static class SceneClassifier
{
    private const string TimelineGuid = "timeline";
    private const string MadevilGuidPrefix = "madevil.";

    public static SceneMetadata Classify(ParsedScene? parsed)
    {
        if (parsed is null) return SceneMetadata.Unknown;
        var scene = parsed.Value;

        bool usesTimeline = false;
        bool isMadevil = false;
        foreach (var guid in scene.PluginGuids)
        {
            if (!usesTimeline && string.Equals(guid, TimelineGuid, StringComparison.OrdinalIgnoreCase))
                usesTimeline = true;
            if (!isMadevil && guid.StartsWith(MadevilGuidPrefix, StringComparison.OrdinalIgnoreCase))
                isMadevil = true;
        }

        var environment = isMadevil ? SceneEnvironment.Madevil : SceneEnvironment.NonMadevil;
        return new SceneMetadata(usesTimeline, environment, scene.Game);
    }
}

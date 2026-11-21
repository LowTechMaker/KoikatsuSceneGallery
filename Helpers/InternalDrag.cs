using Windows.ApplicationModel.DataTransfer;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Marks drags that start inside the app, so the window's import drop can
/// tell them apart from files dragged in from Explorer.
/// </summary>
/// <remarks>
/// Cards dragged out of the gallery carry real files, exactly like a drag from
/// Explorer. Without the mark, every drag of a card toward the game would pass
/// over the window and offer to import the card into the library it came
/// from.
/// </remarks>
internal static class InternalDrag
{
    private const string Key = "KoikatsuSceneGallery.InternalDrag";

    public static void Mark(DataPackage data) => data.Properties[Key] = true;

    public static bool IsInternal(DataPackageView view) => view.Properties.ContainsKey(Key);
}

using KoikatsuSceneGallery.Services;
using Microsoft.UI.Xaml.Controls;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Flipping a card's queue / like / hide flag from a menu, a button or a key:
/// one implementation so every entry point saves and reports the same way.
/// </summary>
internal static class CardStateActions
{
    /// <summary>The label for the action that would flip <paramref name="flag"/> on this card now.</summary>
    public static string Label(string filePath, CardFlag flag)
    {
        var on = App.Services.GetRequiredService<CardStateService>().Has(filePath, flag);
        return UiText.Get(flag switch
        {
            CardFlag.Queued => on ? "CardState_Unqueue" : "CardState_Queue",
            CardFlag.Liked => on ? "CardState_Unlike" : "CardState_Like",
            _ => on ? "CardState_Unhide" : "CardState_Hide",
        });
    }

    /// <summary>Flips the flag on every card to the opposite of the first card's value, then says what happened.</summary>
    public static async Task ToggleAsync(IReadOnlyList<string> filePaths, CardFlag flag)
    {
        if (filePaths.Count == 0) return;
        var states = App.Services.GetRequiredService<CardStateService>();
        var value = !states.Has(filePaths[0], flag);
        if (await states.SetAsync(filePaths, flag, value) > 0)
        {
            AppToast.Show(UiText.Get("CardState_SaveFailed"), InfoBarSeverity.Error, key: "card-state");
            return;
        }

        AppToast.Show(UiText.Get(flag switch
        {
            CardFlag.Queued => value ? "CardState_QueuedToast" : "CardState_UnqueuedToast",
            CardFlag.Liked => value ? "CardState_LikedToast" : "CardState_UnlikedToast",
            _ => value ? "CardState_HiddenToast" : "CardState_UnhiddenToast",
        }), InfoBarSeverity.Success, key: "card-state");
    }
}

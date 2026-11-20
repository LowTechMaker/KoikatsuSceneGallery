using KoikatsuSceneGallery.Services;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The keys that act on a card, the same on every surface that shows one:
/// the library grid, the home page, the viewer and the inspector.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term>Q</term><description>want to try (queued)</description></item>
/// <item><term>F</term><description>like</description></item>
/// <item><term>X</term><description>not for me (hidden)</description></item>
/// <item><term>1 / 2 / 3</term><description>rate G / R-18 / R-18G (scene cards)</description></item>
/// <item><term>Enter</term><description>open</description></item>
/// </list>
/// The letters were the detail page's first (DetailPage.xaml); the home page
/// briefly had Q/L/H, which taught one thing on one page and another on the next.
/// Plain keys only: with Ctrl, Alt, Shift or Win down they belong to someone
/// else (Ctrl+F is search).
/// </remarks>
internal static class CardKeys
{
    /// <summary>The flag a plain key toggles, or null.</summary>
    public static CardFlag? Flag(VirtualKey key) => key switch
    {
        VirtualKey.Q => CardFlag.Queued,
        VirtualKey.F => CardFlag.Liked,
        VirtualKey.X => CardFlag.Hidden,
        _ => null,
    };

    /// <summary>The key's letter as shown in tooltips.</summary>
    public static string Letter(CardFlag flag) => flag switch
    {
        CardFlag.Queued => "Q",
        CardFlag.Liked => "F",
        CardFlag.Hidden => "X",
        _ => "",
    };

    /// <summary>The rating (0 = G, 1 = R-18, 2 = R-18G) a digit key sets, or null.</summary>
    public static int? Rating(VirtualKey key) => key switch
    {
        >= VirtualKey.Number1 and <= VirtualKey.Number3 => key - VirtualKey.Number1,
        >= VirtualKey.NumberPad1 and <= VirtualKey.NumberPad3 => key - VirtualKey.NumberPad1,
        _ => null,
    };

    /// <summary>Whether Ctrl, Alt, Shift or a Windows key is held: then a plain card key is not meant.</summary>
    public static bool IsModifierDown()
    {
        foreach (var key in new[] { VirtualKey.Control, VirtualKey.Menu, VirtualKey.Shift, VirtualKey.LeftWindows, VirtualKey.RightWindows })
        {
            if (InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down))
                return true;
        }
        return false;
    }
}

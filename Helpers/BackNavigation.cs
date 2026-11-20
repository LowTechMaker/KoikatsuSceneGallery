using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// The ways back besides the title bar's button.
/// </summary>
/// <remarks>
/// The title bar's back button is the only one on screen. Pages used to add
/// their own beside their title, so a detail page showed two arrows a few
/// centimetres apart that did the same thing. What replaces the second one is
/// keyboard and mouse: Alt+Left and the mouse's back button everywhere, and Esc
/// on the pages that are a step "into" something (a card, a post, an author).
/// </remarks>
internal static class BackNavigation
{
    /// <summary>Alt+Left, the keyboard Back key and the mouse's back button, for the whole window.</summary>
    public static void Attach(UIElement root, Frame frame)
    {
        void Back(KeyboardAccelerator _, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (!frame.CanGoBack) return;
            frame.GoBack();
            args.Handled = true;
        }

        var altLeft = new KeyboardAccelerator { Key = VirtualKey.Left, Modifiers = VirtualKeyModifiers.Menu };
        altLeft.Invoked += Back;
        var backKey = new KeyboardAccelerator { Key = VirtualKey.GoBack };
        backKey.Invoked += Back;
        root.KeyboardAccelerators.Add(altLeft);
        root.KeyboardAccelerators.Add(backKey);
        // Accelerators would show their hint on every element's tooltip; the
        // keys are a convenience, not something to advertise on each button.
        root.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

        // handledEventsToo: the grid, the image viewer and the lists all mark
        // pointer presses handled, and the back button has to work over them.
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;
            if (!e.GetCurrentPoint(root).Properties.IsXButton1Pressed) return;
            if (!frame.CanGoBack) return;
            frame.GoBack();
            e.Handled = true;
        }), handledEventsToo: true);
    }

    /// <summary>Esc leaves <paramref name="page"/>, unless the user is typing.</summary>
    public static void EscapeGoesBack(Page page)
    {
        var escape = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escape.Invoked += (_, args) =>
        {
            // Esc in a text box means "never mind this edit", not "leave".
            if (page.XamlRoot is { } root && CardKeys.IsTyping(root)) return;
            if (page.Frame is not { CanGoBack: true } frame) return;
            frame.GoBack();
            args.Handled = true;
        };
        page.KeyboardAccelerators.Add(escape);
        page.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }
}

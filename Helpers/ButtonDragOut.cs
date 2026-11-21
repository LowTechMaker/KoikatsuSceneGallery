using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>Lets a card that is a <see cref="Button"/> be dragged out to the game.</summary>
/// <remarks>
/// <see cref="UIElement.CanDrag"/> does nothing on a button: the button
/// captures the pointer when pressed, so the system never sees the gesture
/// that would start a drag. The press and the movement are watched here
/// instead (handled events included, since the button marks them handled),
/// and once the pointer has moved past the system's drag threshold with the
/// button held, the drag is started by hand. Releasing the capture first
/// also cancels the click the press would otherwise end in.
/// </remarks>
internal static class ButtonDragOut
{
    // The system's own distance (SM_CXDRAG) is 4 px; a little more keeps an
    // unsteady click from turning into a drag.
    private const double Threshold = 6;

    /// <param name="filePath">The card to drag now, or null when there is none.</param>
    /// <param name="operation">The name the drag is recorded under.</param>
    public static void Enable(Button button, Func<string?> filePath, string operation)
    {
        Point? pressedAt = null;
        var dragging = false;

        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(button);
            // Touch pans the page or the row; only a mouse or pen drags a card out.
            pressedAt = e.Pointer.PointerDeviceType != PointerDeviceType.Touch && point.Properties.IsLeftButtonPressed
                ? point.Position
                : null;
        }), handledEventsToo: true);

        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => pressedAt = null),
            handledEventsToo: true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => pressedAt = null),
            handledEventsToo: true);

        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(async (_, e) =>
        {
            if (pressedAt is not { } start || dragging) return;
            var point = e.GetCurrentPoint(button);
            if (!point.Properties.IsLeftButtonPressed)
            {
                pressedAt = null;
                return;
            }
            if (Math.Abs(point.Position.X - start.X) < Threshold && Math.Abs(point.Position.Y - start.Y) < Threshold)
                return;
            if (filePath() is null) return;

            pressedAt = null;
            dragging = true;
            try
            {
                button.ReleasePointerCaptures();
                await button.StartDragAsync(point);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A drag that could not start leaves the card where it was.
                App.Services.GetService<Services.IAppLogger>()?.LogError(operation, ex);
            }
            finally
            {
                dragging = false;
            }
        }), handledEventsToo: true);

        button.DragStarting += (_, e) =>
        {
            if (filePath() is { } path) DragFilePayload.Attach(e, operation, path);
            else e.Cancel = true;
        };
    }
}

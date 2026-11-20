using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Short notices about something that just happened - "copied", "could not
/// save" - shown at the window's bottom right and dismissed on their own.
/// </summary>
/// <remarks>
/// <para>For events, not state. A line that describes how things are (no
/// rating yet, a card that cannot be rated) stays on the page next to the
/// thing it describes; a toast is for the moment an action finished or
/// failed, which used to be written into whatever caption happened to be
/// nearby and then overwritten by the next refresh.</para>
/// <para>Notices with the same <c>key</c> replace each other, so rating a run
/// of cards that all fail leaves one notice rather than a column of them.</para>
/// </remarks>
internal static class AppToast
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan ShortDuration = AppTiming.ToastShort;
    private static readonly TimeSpan LongDuration = AppTiming.ToastLong;

    private static Panel? _host;
    private static DispatcherQueue? _dispatcher;

    /// <summary>Called once by the main window with the panel the notices stack in.</summary>
    public static void Attach(Panel host)
    {
        _host = host;
        _dispatcher = host.DispatcherQueue;
    }

    public static void Show(
        string message,
        InfoBarSeverity severity = InfoBarSeverity.Informational,
        string? title = null,
        string? key = null,
        bool sticky = false)
    {
        if (_dispatcher is not { } dispatcher) return;
        if (!dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => Show(message, severity, title, key, sticky));
            return;
        }

        if (_host is not { } host) return;
        if (key is not null)
        {
            foreach (var existing in host.Children.OfType<FrameworkElement>().ToList())
            {
                if (Equals(existing.Tag, key)) host.Children.Remove(existing);
            }
        }

        while (host.Children.Count >= MaxVisible) host.Children.RemoveAt(0);

        var bar = new InfoBar
        {
            Title = title ?? "",
            Message = message,
            Severity = severity,
            IsClosable = true,
            IsOpen = true,
            BorderThickness = new Thickness(0),
        };
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["FloatingSurfaceStyle"],
            Child = bar,
            Tag = key,
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 0, 32),
        };
        bar.Closed += (_, _) => host.Children.Remove(card);

        // Problems stay up long enough to read a sentence; confirmations do not
        // need to. A pointer over the notice holds it, so it never vanishes
        // while being read.
        var timer = dispatcher.CreateTimer();
        timer.Interval = severity is InfoBarSeverity.Error or InfoBarSeverity.Warning ? LongDuration : ShortDuration;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => host.Children.Remove(card);
        card.Unloaded += (_, _) => timer.Stop();
        // Sticky: the summary of something that ran for hours, which the user
        // may well not have been watching when it ended. It stays until closed,
        // so it gets no timer and nothing that could start one.
        if (!sticky)
        {
            card.PointerEntered += (_, _) => timer.Stop();
            card.PointerExited += (_, _) => timer.Start();
        }

        host.Children.Add(card);
        if (!sticky) timer.Start();
    }
}

using System.Diagnostics;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Collects each library's first load of this run into <c>startup_timing.log</c>
/// (<see cref="StartupTimingLog"/>): names and numbers only. Each library writes
/// its lines once, when its first load has finished; reloads are not logged.
/// </summary>
internal static class StartupTimingRecorder
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Written = [];
    private static readonly DateTime ProcessStarted = ReadProcessStart();

    /// <summary>One id per run, shared by every library's lines.</summary>
    public static string StartId { get; } =
        $"{ProcessStarted:yyyyMMdd-HHmmss}-{Environment.ProcessId}";

    /// <summary>Milliseconds since the process started: for the "first cards" and "load finished" moments.</summary>
    public static long SinceProcessStart => (long)(DateTime.Now - ProcessStarted).TotalMilliseconds;

    private static DateTime ReadProcessStart()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch (Exception) { return DateTime.Now; }
    }

    /// <summary>Whether <paramref name="library"/> has not logged yet this run (its first load is the start).</summary>
    public static bool IsFirstLoad(string library)
    {
        lock (Gate) return !Written.Contains(library);
    }

    /// <summary>Appends the library's phases, once per run, off the calling thread.</summary>
    public static void Write(string library, IReadOnlyList<StartupPhase> phases)
    {
        lock (Gate)
        {
            if (!Written.Add(library)) return;
        }
        var at = DateTimeOffset.Now;
        _ = Task.Run(() =>
        {
            // One writer at a time: the three libraries finish close together.
            lock (Gate)
                StartupTimingLog.Append(Path.Combine(AppPaths.LocalFolder, StartupTimingLog.FileName), StartId, at, phases);
        });
    }
}

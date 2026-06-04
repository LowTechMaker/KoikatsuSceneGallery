using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// Appends unhandled-exception details to a crash log under the app's local data
/// folder. There is no debugger attached in a packaged-zip test build, so this
/// is the only way a tester can hand back a stack trace. Best-effort: any failure
/// to write is swallowed so logging can never itself crash the app.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(AppPaths.LocalFolder, "crash.log");

    public static void Write(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var entry =
                $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ===={Environment.NewLine}" +
                ex + Environment.NewLine + Environment.NewLine;
            lock (Gate)
                File.AppendAllText(LogPath, entry);
        }
        catch
        {
            // Never let the logger throw.
        }
    }
}

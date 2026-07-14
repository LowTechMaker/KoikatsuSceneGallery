using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

public sealed class CrashLogLogger : IAppLogger
{
    public void LogError(string operation, Exception exception, string? path = null)
    {
        var context = string.IsNullOrWhiteSpace(path)
            ? operation
            : $"{operation} [{path}]";
        CrashLog.Write(context, exception);
    }
}

using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

public static class UiEventGuard
{
    public static void Run(IAppLogger logger, string operation, Func<Task> action)
        => Run(logger, operation, action, onError: null);

    public static void Run(
        IAppLogger logger,
        string operation,
        Func<Task> action,
        Action<Exception>? onError) =>
        RunCoreAsync(logger, operation, action, onError)
            .Observe(logger, $"{operation}.Guard");

    private static async Task RunCoreAsync(
        IAppLogger logger,
        string operation,
        Func<Task> action,
        Action<Exception>? onError)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(operation, ex);
            onError?.Invoke(ex);
        }
    }
}

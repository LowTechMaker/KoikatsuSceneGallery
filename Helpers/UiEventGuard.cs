using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

public static class UiEventGuard
{
    public static void Run(IAppLogger logger, string operation, Func<Task> action)
        => RunCoreAsync(logger, operation, action).Observe(logger, $"{operation}.Guard");

    private static async Task RunCoreAsync(IAppLogger logger, string operation, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            logger.LogError(operation, ex);
        }
    }
}

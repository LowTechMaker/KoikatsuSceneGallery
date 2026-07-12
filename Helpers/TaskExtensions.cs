using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

public static class TaskExtensions
{
    public static void Observe(this Task task, IAppLogger logger, string operation)
    {
        task.ContinueWith(
            completed => logger.LogError(operation, completed.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}

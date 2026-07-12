namespace KoikatsuSceneGallery.Helpers;

internal static class BoundedAsyncPipeline
{
    public static Task ForEachAsync<T>(
        IEnumerable<T> items,
        int maxDegreeOfParallelism,
        Func<T, CancellationToken, ValueTask> processor,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

        return Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
            },
            processor);
    }
}

using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Helpers;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.Services;

public sealed class ThumbnailService
{
    private readonly ThumbnailCacheService _cacheService;
    private readonly SceneCardCacheService _sceneCardCacheService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly ThumbnailCacheActivity _cacheActivity;
    private readonly SemaphoreSlim _generationGate =
        new(Math.Clamp(Environment.ProcessorCount - 1, 1, 4));

    public ThumbnailService(
        ThumbnailCacheService cacheService,
        SceneCardCacheService sceneCardCacheService,
        DispatcherQueue dispatcherQueue,
        ThumbnailCacheActivity cacheActivity)
    {
        _cacheService = cacheService;
        _sceneCardCacheService = sceneCardCacheService;
        _dispatcherQueue = dispatcherQueue;
        _cacheActivity = cacheActivity;
    }

    public Task EnsureThumbnailAsync(
        CardBase card,
        CancellationToken cancellationToken = default) =>
        EnsureThumbnailAsync(card, isVideo: false, cancellationToken);

    public Task EnsureVideoThumbnailAsync(
        MediaCard card,
        CancellationToken cancellationToken = default) =>
        EnsureThumbnailAsync(card, isVideo: true, cancellationToken);

    private async Task EnsureThumbnailAsync(
        CardBase card,
        bool isVideo,
        CancellationToken cancellationToken)
    {
        using var shutdownLinkedCts = CancellationTokenSource
            .CreateLinkedTokenSource(cancellationToken, _cacheActivity.ShutdownToken);
        var workToken = shutdownLinkedCts.Token;
        workToken.ThrowIfCancellationRequested();
        if (!card.NeedsThumbnail)
            return;

        using var cacheActivity = _cacheActivity.Begin();
        await _generationGate.WaitAsync(workToken).ConfigureAwait(false);
        try
        {
            workToken.ThrowIfCancellationRequested();
            if (!card.NeedsThumbnail)
                return;

            var cachedPath = await Task.Run(async () =>
            {
                var path = _cacheService.TryGetCachedPath(
                    card.FilePath,
                    card.FileSize,
                    card.DateModified);
                if (path is not null)
                    return path;

                return isVideo
                    ? await _cacheService.EnsureVideoThumbnailAsync(
                        card.FilePath,
                        card.FileSize,
                        card.DateModified,
                        workToken).ConfigureAwait(false)
                    : await _cacheService.EnsureThumbnailAsync(
                        card.FilePath,
                        card.FileSize,
                        card.DateModified,
                        workToken).ConfigureAwait(false);
            }, workToken).ConfigureAwait(false);

            if (cachedPath is null)
            {
                card.ThumbnailGenerationFailed = true;
                return;
            }

            workToken.ThrowIfCancellationRequested();
            if (card is SceneCard)
            {
                _sceneCardCacheService.SetThumbnailPath(
                    card.FilePath,
                    card.FileSize,
                    card.DateModified.Ticks,
                    cachedPath);
            }
            await SetThumbnailPathAsync(card, cachedPath, workToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    private async Task SetThumbnailPathAsync(
        CardBase card,
        string thumbnailPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcherQueue.HasThreadAccess)
        {
            card.ThumbnailPath = thumbnailPath;
            return;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));
        if (!_dispatcherQueue.TryEnqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return;
                }

                card.ThumbnailPath = thumbnailPath;
                completion.TrySetResult();
            }))
        {
            completion.TrySetException(
                new InvalidOperationException("Unable to dispatch thumbnail update."));
        }

        await completion.Task.ConfigureAwait(false);
    }
}

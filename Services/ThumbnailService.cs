using KoikatsuSceneGallery.Models;
using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.Services;

public sealed class ThumbnailService
{
    private readonly ThumbnailCacheService _cacheService;
    private readonly SceneCardCacheService _sceneCardCacheService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SemaphoreSlim _generationGate =
        new(Math.Max(1, Environment.ProcessorCount - 1));

    public ThumbnailService(
        ThumbnailCacheService cacheService,
        SceneCardCacheService sceneCardCacheService,
        DispatcherQueue dispatcherQueue)
    {
        _cacheService = cacheService;
        _sceneCardCacheService = sceneCardCacheService;
        _dispatcherQueue = dispatcherQueue;
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
        cancellationToken.ThrowIfCancellationRequested();
        if (card.HasThumbnail)
            return;

        var cachedPath = _cacheService.TryGetCachedPath(card.FilePath, card.DateModified);
        if (cachedPath is not null)
        {
            await SetThumbnailPathAsync(card, cachedPath, cancellationToken);
            return;
        }

        await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (card.HasThumbnail)
                return;

            cachedPath = _cacheService.TryGetCachedPath(card.FilePath, card.DateModified);
            if (cachedPath is null)
            {
                cachedPath = isVideo
                    ? await _cacheService
                        .EnsureVideoThumbnailAsync(
                            card.FilePath,
                            card.DateModified,
                            cancellationToken)
                        .ConfigureAwait(false)
                    : await _cacheService
                        .EnsureThumbnailAsync(
                            card.FilePath,
                            card.DateModified,
                            cancellationToken)
                        .ConfigureAwait(false);
            }

            if (cachedPath is null)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            if (card is SceneCard)
                _sceneCardCacheService.SetThumbnailPath(card.FilePath, cachedPath);
            await SetThumbnailPathAsync(card, cachedPath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    private Task SetThumbnailPathAsync(
        CardBase card,
        string thumbnailPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcherQueue.HasThreadAccess)
        {
            card.ThumbnailPath = thumbnailPath;
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
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

        return completion.Task;
    }
}

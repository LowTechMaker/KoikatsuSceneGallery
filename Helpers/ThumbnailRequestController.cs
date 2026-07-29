using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Helpers;

internal sealed class ThumbnailRequestController
{
    private readonly ThumbnailService _thumbnailService;
    private readonly IAppLogger _logger;
    private readonly string _operation;
    private readonly Action<int>? _pendingCountChanged;
    private readonly ThumbnailRequestScope _scope = new();
    private int _pendingCount;

    public ThumbnailRequestController(
        ThumbnailService thumbnailService,
        IAppLogger logger,
        string operation,
        Action<int>? pendingCountChanged = null)
    {
        _thumbnailService = thumbnailService;
        _logger = logger;
        _operation = operation;
        _pendingCountChanged = pendingCountChanged;
    }

    public void Activate()
    {
        _scope.Activate();
        SetPendingCount(0);
    }

    public void Cancel()
    {
        _scope.Cancel();
        SetPendingCount(0);
    }

    public void Request(CardBase card, bool isVideo = false)
    {
        if (!card.NeedsThumbnail || !_scope.TryBegin(card.FilePath))
            return;

        var cancellationToken = _scope.Token;
        SetPendingCount(_pendingCount + 1);
        LoadAsync(card, isVideo, cancellationToken)
            .Observe(_logger, _operation);
    }

    private async Task LoadAsync(
        CardBase card,
        bool isVideo,
        CancellationToken cancellationToken)
    {
        try
        {
            if (isVideo && card is MediaCard mediaCard)
                await _thumbnailService.EnsureVideoThumbnailAsync(
                    mediaCard,
                    cancellationToken);
            else
                await _thumbnailService.EnsureThumbnailAsync(
                    card,
                    cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _scope.Complete(
                    card.FilePath,
                    card.HasThumbnail || card.ThumbnailGenerationFailed);
                SetPendingCount(Math.Max(0, _pendingCount - 1));
            }
        }
    }

    private void SetPendingCount(int value)
    {
        _pendingCount = value;
        _pendingCountChanged?.Invoke(value);
    }
}

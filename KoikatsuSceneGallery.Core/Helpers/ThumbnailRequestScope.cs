namespace KoikatsuSceneGallery.Helpers;

internal sealed class ThumbnailRequestScope : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly HashSet<string> _requestKeys = new(StringComparer.OrdinalIgnoreCase);

    public CancellationToken Token =>
        _cancellationTokenSource?.Token ?? new CancellationToken(canceled: true);

    public void Activate()
    {
        Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public bool TryBegin(string requestKey)
    {
        var source = _cancellationTokenSource;
        return source is { IsCancellationRequested: false }
               && _requestKeys.Add(requestKey);
    }

    public void Complete(string requestKey, bool succeeded)
    {
        if (!succeeded)
            _requestKeys.Remove(requestKey);
    }

    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _requestKeys.Clear();
    }

    public void Dispose()
    {
        Cancel();
        GC.SuppressFinalize(this);
    }
}

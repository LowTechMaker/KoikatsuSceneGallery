namespace KoikatsuSceneGallery.Helpers;

internal sealed class ThumbnailRequestScope : IDisposable
{
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly HashSet<string> _requestedPaths = new(StringComparer.OrdinalIgnoreCase);

    public CancellationToken Token =>
        _cancellationTokenSource?.Token ?? new CancellationToken(canceled: true);

    public void Activate()
    {
        Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
    }

    public bool TryBegin(string filePath)
    {
        var source = _cancellationTokenSource;
        return source is { IsCancellationRequested: false }
               && _requestedPaths.Add(filePath);
    }

    public void Complete(string filePath, bool succeeded)
    {
        if (!succeeded)
            _requestedPaths.Remove(filePath);
    }

    public void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _requestedPaths.Clear();
    }

    public void Dispose()
    {
        Cancel();
        GC.SuppressFinalize(this);
    }
}

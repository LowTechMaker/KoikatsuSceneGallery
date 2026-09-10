using Microsoft.UI.Dispatching;

namespace KoikatsuSceneGallery.Services;

// Used on the owning UI dispatcher. The callback retains each gallery's filter policy.
internal sealed class GalleryMetadataRefresh(DispatcherQueue dispatcher, Action refresh) : IDisposable
{
    private DispatcherQueueTimer? _timer;
    private bool _disposed;

    public void Start()
    {
        if (_disposed) return;
        _timer ??= dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(750);
        _timer.IsRepeating = true;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (!_disposed) refresh();
    }

    public void Stop() => _timer?.Stop();

    public void Complete()
    {
        Stop();
        if (!_disposed) refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_timer is not null) _timer.Tick -= OnTick;
    }
}

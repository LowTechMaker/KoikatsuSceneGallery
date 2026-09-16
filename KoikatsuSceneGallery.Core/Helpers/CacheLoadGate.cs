namespace KoikatsuSceneGallery.Helpers;

internal sealed class CacheLoadGate
{
    private readonly Lock _gate = new();
    private volatile bool _loaded;

    // true means the owner considers this attempt final (including handled failures).
    // false leaves it retryable. Other callers cannot pass until the attempt finishes.
    internal void EnsureLoaded(Func<bool> load)
    {
        if (_loaded) return;
        lock (_gate)
        {
            if (_loaded) return;
            _loaded = load();
        }
    }
}

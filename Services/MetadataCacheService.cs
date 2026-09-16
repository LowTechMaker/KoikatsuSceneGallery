using System.Collections.Concurrent;
using System.Text.Json;
using KoikatsuSceneGallery.Models;
using SceneGallery.PluginCommon;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

public abstract class MetadataCacheService<TCard, TMetadata>
    where TCard : CardBase
    where TMetadata : class
{
    private readonly string _cachePath;
    private readonly IAppLogger _logger;
    private readonly ConcurrentDictionary<string, TMetadata> _cache = new();
    private readonly JsonSerializerOptions? _jsonOptions;

    private readonly DebouncedDiskPersistence _persistence;
    private readonly OperationDrain _operations = new();

    private readonly CacheLoadGate _loadGate = new();

    protected MetadataCacheService(IAppLogger logger, string cacheFileName, JsonSerializerOptions? jsonOptions = null)
        : this(logger, Path.Combine(AppPaths.LocalFolder, cacheFileName), jsonOptions,
            (path, serialize, report) => new DebouncedDiskPersistence(path, serialize, report))
    {
    }

    internal MetadataCacheService(IAppLogger logger, string cachePath, JsonSerializerOptions? jsonOptions,
        Func<string, Action<Stream>, Action<Exception>, DebouncedDiskPersistence> createPersistence)
    {
        _logger = logger;
        _cachePath = cachePath;
        _jsonOptions = jsonOptions;
        _persistence = createPersistence(_cachePath,
            stream => JsonSerializer.Serialize(stream, new Dictionary<string, TMetadata>(_cache), _jsonOptions),
            ex => _logger.LogError("MetadataCache.Flush", ex, _cachePath));
    }

    protected abstract TMetadata Parse(TCard card);

    private void EnsureLoaded()
    {
        _loadGate.EnsureLoaded(() =>
        {
            try
            {
                if (!File.Exists(_cachePath)) return true;
                using var stream = File.OpenRead(_cachePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, TMetadata>>(stream, _jsonOptions);
                if (data is null) return true;
                foreach (var (key, value) in data)
                    _cache[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError("MetadataCache.Load", ex, _cachePath);
            }
            return true;
        });
    }

    public bool TryGetCached(TCard card, out TMetadata metadata)
    {
        _operations.TryRun(EnsureLoaded);
        var key = FileVersionCacheKey.Compute(card.FilePath, card.DateModified);
        return _cache.TryGetValue(key, out metadata!);
    }

    public TMetadata ParseAndCache(TCard card, CancellationToken cancellationToken = default)
    {
        TMetadata result = null!;
        if (!_operations.TryRun(() => result = ParseAndCacheCore(card, cancellationToken)))
            throw new ObjectDisposedException(GetType().Name);
        return result;
    }

    private TMetadata ParseAndCacheCore(TCard card, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = FileVersionCacheKey.Compute(card.FilePath, card.DateModified);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var metadata = Parse(card);
        cancellationToken.ThrowIfCancellationRequested();
        _cache[key] = metadata;
        ScheduleSave();
        return metadata;
    }

    public void Invalidate(TCard card)
    {
        _operations.TryRun(() =>
        {
            var key = FileVersionCacheKey.Compute(card.FilePath, card.DateModified);
            if (_cache.TryRemove(key, out _))
                ScheduleSave();
        });
    }

    private void ScheduleSave() => _persistence.MarkDirty();

    internal Task StopAsync() => _operations.StopAsync();

    // Misordered ownership must fail explicitly instead of disposing under a producer.
    internal void DisposePersistence()
    {
        if (!StopAsync().IsCompletedSuccessfully)
            throw new InvalidOperationException("Metadata operations must drain before persistence disposal.");
        _persistence.Dispose();
    }
}

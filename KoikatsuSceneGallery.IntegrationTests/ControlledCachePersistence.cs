using KoikatsuSceneGallery.Services;
using SceneGallery.PluginCommon;

namespace KoikatsuSceneGallery.IntegrationTests;

// Shares the production serializer while keeping all writes in memory.
internal class ControlledCachePersistence : IDisposable, IAppLogger
{
    internal string CachePath { get; } = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.json");
    internal readonly List<string> Errors = [];
    internal DebouncedDiskPersistence Persistence = null!;
    internal bool FailWrite;
    internal byte[]? Json;
    internal Action? AfterSerialize;
    internal ControlledCachePersistence Logger => this;

    internal DebouncedDiskPersistence Create(string path, Action<Stream> serialize, Action<Exception> report)
        => Persistence = new(path, serialize, report, (_, write) =>
        {
            if (FailWrite) throw new IOException("controlled failure");
            using var stream = new MemoryStream();
            write(stream);
            Json = stream.ToArray();
            AfterSerialize?.Invoke();
        });

    public void LogError(string operation, Exception exception, string? path = null) => Errors.Add(operation);

    public void Dispose()
    {
        AfterSerialize = null;
        FailWrite = false;
        Persistence?.Dispose();
    }
}

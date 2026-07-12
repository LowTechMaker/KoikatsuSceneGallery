namespace KoikatsuSceneGallery.Services;

public sealed class AppServiceRegistry
{
    private readonly Dictionary<(Type Type, string? Key), object> _services = [];

    internal void Add<T>(T service, string? key = null) where T : class
        => _services.Add((typeof(T), key), service);

    public T GetRequiredService<T>(string? key = null) where T : class
        => GetService<T>(key)
           ?? throw new InvalidOperationException($"Service {typeof(T).Name} ({key ?? "default"}) is not registered.");

    public T? GetService<T>(string? key = null) where T : class
        => _services.TryGetValue((typeof(T), key), out var service) ? (T)service : null;
}

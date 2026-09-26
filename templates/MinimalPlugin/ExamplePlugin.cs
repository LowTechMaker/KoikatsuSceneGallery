using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.Example;

/// <summary>SDK adapter and composition root. The bundled source performs no network I/O.</summary>
public sealed class ExamplePlugin : IFolderAuthorProvider, IDisposable
{
    private readonly ExampleAuthorService _authors;
    private bool _disposed;
    public ExamplePlugin() : this(new ExampleAuthorService(new FakeAuthorSource())) { }
    internal ExamplePlugin(ExampleAuthorService authors) => _authors = authors;
    public string Name => "Example";
    public string Version => typeof(ExamplePlugin).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    public string ProviderId => "example";
    public void Initialize(IPluginHost host) => host.Log("Example plugin uses local fake data.");
    public ParsedAuthor? TryParseFolderName(string folderName)
        => ExampleFolderParser.ParseId(folderName) is { } id
            ? new ParsedAuthor(new AuthorKey(ProviderId, id), folderName) : null;
    public Task<AuthorInfo?> GetAuthorInfoAsync(AuthorKey key, bool forceRefresh, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return key.ProviderId == ProviderId ? _authors.GetAsync(key, ct) : Task.FromResult<AuthorInfo?>(null);
    }
    public string GetProfileUrl(AuthorKey key) => ExampleAuthorService.ProfileUrl(key.Id);
    public void Dispose() => _disposed = true;
}

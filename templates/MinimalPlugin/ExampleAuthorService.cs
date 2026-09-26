using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.Example;

internal sealed class ExampleAuthorService(FakeAuthorSource source)
{
    internal async Task<AuthorInfo?> GetAsync(AuthorKey key, CancellationToken ct)
    {
        var name = await source.GetNameAsync(key.Id, ct).ConfigureAwait(false);
        return name is null ? null : new AuthorInfo(key, name, null, ProfileUrl(key.Id), DateTimeOffset.UtcNow);
    }
    internal static string ProfileUrl(string id) => "https://example.invalid/authors/" + Uri.EscapeDataString(id);
}

internal sealed class FakeAuthorSource
{
    internal Task<string?> GetNameAsync(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(id == "42" ? "Example Author" : null);
    }
}

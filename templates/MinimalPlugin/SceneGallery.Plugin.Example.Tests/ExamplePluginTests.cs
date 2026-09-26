using NetArchTest.Rules;
using SceneGallery.PluginSdk;

namespace SceneGallery.Plugin.Example.Tests;

public sealed class ExamplePluginTests
{
    [Fact]
    public void Sdk13CapabilitiesKeepTheOneMajorRuntimeIdentity()
    {
        Assert.Equal(new Version(1, 0, 0, 0), typeof(IPluginHost).Assembly.GetName().Version);
        Assert.Equal(typeof(string), typeof(IPluginHost).GetProperty(nameof(IPluginHost.Language))?.PropertyType);
        Assert.Equal("en", ((IPluginHost)new LegacyHost()).Language);
        Assert.True(typeof(IPlugin).IsAssignableFrom(typeof(ITagDictionaryProvider)));
    }

    [Fact]
    public async Task ParsedAuthorResolvesUsingFakeSource()
    {
        using var plugin = new ExamplePlugin();
        var parsed = plugin.TryParseFolderName("example_42");
        Assert.NotNull(parsed);
        var author = await plugin.GetAuthorInfoAsync(parsed.Key, false, CancellationToken.None);
        Assert.Equal("Example Author", author?.Name);
        Assert.Equal("https://example.invalid/authors/42", author?.ProfileUrl);
    }

    [Theory]
    [InlineData("example_")]
    [InlineData("example_abc")]
    [InlineData("other_42")]
    public void ParserRejectsUnrecognizedFolder(string name)
    {
        using var plugin = new ExamplePlugin();
        Assert.Null(plugin.TryParseFolderName(name));
    }

    [Fact]
    public async Task MissingOrForeignAuthorReturnsNull()
    {
        using var plugin = new ExamplePlugin();
        Assert.Null(await plugin.GetAuthorInfoAsync(new AuthorKey("other", "42"), false, CancellationToken.None));
        Assert.Null(await plugin.GetAuthorInfoAsync(new AuthorKey("example", "99"), false, CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAndDisposalAreNotReportedAsMissing()
    {
        using var plugin = new ExamplePlugin();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => plugin.GetAuthorInfoAsync(new AuthorKey("example", "42"), false, new CancellationToken(true)));
        plugin.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => plugin.GetAuthorInfoAsync(new AuthorKey("example", "42"), false, CancellationToken.None));
    }

    [Fact]
    public void ArchitectureHasOnePublicEntryAndPureParser()
    {
        var assembly = typeof(ExamplePlugin).Assembly;
        Assert.Equal([typeof(ExamplePlugin)], assembly.GetExportedTypes());
        var parser = Types.InAssembly(assembly).That().HaveName(nameof(ExampleFolderParser));
        Assert.Single(parser.GetTypes());
        Assert.True(parser.ShouldNot().HaveDependencyOnAny("System.Net.Http", "System.IO.File", typeof(ExamplePlugin).FullName!, typeof(ExampleAuthorService).FullName!).GetResult().IsSuccessful);
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("KoikatsuSceneGallery") || reference.Name.StartsWith("NetArchTest"));
    }

    private sealed class LegacyHost : IPluginHost
    {
        public string StorageDirectory => string.Empty;
        public void Log(string message) { }
    }
}

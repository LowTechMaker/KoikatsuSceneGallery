using SceneGallery.PluginCommon;
using SceneGallery.PluginSdk;

namespace SceneGallery.PluginPackages.Validation;

internal static class PackageProbe
{
    internal static (string Language, TagArticle? CachedTag) CompileSdk13Assets(
        IPluginHost host,
        ITagDictionaryProvider provider)
    {
        var language = host.Language;
        return (language, provider.TryGetCached("example", language));
    }

    internal static async Task<AuthorKey> CompileAllPackageAssetsAsync(
        string path,
        CancellationToken ct)
    {
        var limiter = new RateLimiter(TimeSpan.Zero);
        using var lease = await limiter.AcquireAsync(ct).ConfigureAwait(false);

        using var persistence = new DebouncedDiskPersistence(
            path,
            _ => { },
            _ => { });
        persistence.MarkDirty();

        using var drain = new PluginOperationDrain(
            "package-validation", TimeSpan.Zero, () => { },
            deadline => { _ = deadline.Remaining; return Task.CompletedTask; },
            () => { }, _ => { });
        _ = await drain.RunProducerAsync(() => Task.FromResult(1));
        drain.Dispose();
        await drain.Completion;

        var protectedValue = DpapiSecretProtector.Protect("package-validation");
        _ = DpapiSecretProtector.Unprotect(
            protectedValue,
            "PackageValidation",
            _ => { },
            out _);

        return new AuthorKey("package-validation", "1");
    }
}

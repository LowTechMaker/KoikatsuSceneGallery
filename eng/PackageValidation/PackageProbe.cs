using SceneGallery.PluginCommon;
using SceneGallery.PluginSdk;

namespace SceneGallery.PluginPackages.Validation;

internal static class PackageProbe
{
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

        var protectedValue = DpapiSecretProtector.Protect("package-validation");
        _ = DpapiSecretProtector.Unprotect(
            protectedValue,
            "PackageValidation",
            _ => { },
            out _);

        return new AuthorKey("package-validation", "1");
    }
}

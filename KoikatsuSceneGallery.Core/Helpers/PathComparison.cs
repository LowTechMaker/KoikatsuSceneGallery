namespace KoikatsuSceneGallery.Helpers;

/// <summary>
/// How two file system paths are compared on the running platform.
/// </summary>
/// <remarks>
/// Windows paths are case insensitive and every other platform this code can
/// run on is case sensitive. The rule was written out at each site, which meant
/// nine copies of the same conditional and no single place to correct it.
/// Only the comparison rule is shared: normalization, full-path expansion and
/// separator handling stay with the caller, because they differ by operation.
/// </remarks>
internal static class PathComparison
{
    /// <summary>The comparer for dictionaries, sets and LINQ operators keyed by path.</summary>
    internal static StringComparer Comparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>The comparison for direct <see cref="string.Equals(string?, string?, StringComparison)"/> calls.</summary>
    internal static StringComparison Comparison { get; } = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}

namespace KoikatsuSceneGallery.Tests;

/// <summary>
/// A fact that only runs on Windows, and is skipped elsewhere.
/// </summary>
/// <remarks>
/// The Core test project runs on Linux in CI as well, where a handful of cases
/// cannot hold: they assert on Windows file system semantics — a backslash as a
/// separator, or a rollback whose behaviour differs off Windows. The product
/// itself is a WinUI application and only ever runs on Windows, so the value of
/// these cases is the Windows behaviour they pin, not cross-platform coverage.
///
/// Use this only where the case genuinely depends on the platform. A case that
/// merely spells a path with backslashes but never parses it still runs
/// everywhere, and should keep a plain <see cref="FactAttribute"/>.
/// </remarks>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only: the case asserts Windows file system semantics.";
    }
}

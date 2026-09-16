using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class PathComparisonTests
{
    [Fact]
    public void ComparerAndComparisonAgreeWithThePlatformRule()
    {
        var caseInsensitive = OperatingSystem.IsWindows();

        Assert.Equal(caseInsensitive, PathComparison.Comparer.Equals(@"C:\Cards\A.png", @"c:\cards\a.png"));
        Assert.Equal(
            caseInsensitive,
            string.Equals(@"C:\Cards\A.png", @"c:\cards\a.png", PathComparison.Comparison));
    }

    [Fact]
    public void DifferentPathsNeverMatchAndIdenticalPathsAlwaysDo()
    {
        Assert.True(PathComparison.Comparer.Equals(@"C:\Cards\a.png", @"C:\Cards\a.png"));
        Assert.False(PathComparison.Comparer.Equals(@"C:\Cards\a.png", @"C:\Cards\b.png"));
        Assert.True(string.Equals(@"C:\Cards\a.png", @"C:\Cards\a.png", PathComparison.Comparison));
        Assert.False(string.Equals(@"C:\Cards\a.png", @"C:\Cards\b.png", PathComparison.Comparison));
    }

    [Fact]
    public void ComparerHashesMatchingPathsAlike()
    {
        // A set or dictionary keyed by path relies on the hash agreeing with Equals.
        var set = new HashSet<string>(PathComparison.Comparer) { @"C:\Cards\A.png" };

        Assert.Equal(OperatingSystem.IsWindows(), set.Contains(@"c:\cards\a.png"));
        Assert.True(set.Contains(@"C:\Cards\A.png"));
    }
}

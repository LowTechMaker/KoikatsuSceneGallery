using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class ArtworkDirectoryLookupTests
{
    [Fact]
    public void EmptyCandidatesDoNotCallParser()
        => Assert.Null(ArtworkDirectoryLookup.FindFirst([], "provider", "work",
            _ => throw new InvalidOperationException()));

    [Theory]
    [InlineData(null, null)]
    [InlineData("other", "work")]
    [InlineData("provider", "other")]
    public void NonmatchingIdentityReturnsNull(string? provider, string? id)
        => Assert.Null(ArtworkDirectoryLookup.FindFirst([Path.Combine("root", "folder")], "provider", "work",
            _ => provider is null ? null : (provider, id!)));

    [Fact]
    public void FirstMatchPreservesInputOrderAndStopsEnumeration()
    {
        var disposed = false;
        IEnumerable<string> Candidates()
        {
            try
            {
                yield return Path.Combine("root", "unknown");
                yield return Path.Combine("root", "z-first");
                throw new InvalidOperationException("Must stop before later candidates");
            }
            finally { disposed = true; }
        }
        var names = new List<string>();
        var result = ArtworkDirectoryLookup.FindFirst(Candidates(), "provider", "work", name =>
        {
            names.Add(name);
            return name == "unknown" ? null : ("PROVIDER", "WORK");
        });
        Assert.Equal(Path.Combine("root", "z-first"), result);
        Assert.Equal(new[] { "unknown", "z-first" }, names);
        Assert.True(disposed);
    }

    [Fact]
    public void MultipleMatchesAreNotSorted()
        => Assert.Equal("z", ArtworkDirectoryLookup.FindFirst(["z", "a"], "p", "id", _ => ("p", "id")));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParserExceptionsPropagateUnchanged(bool cancellation)
    {
        Exception error = cancellation ? new OperationCanceledException() : new IOException("parse");
        var actual = Record.Exception(() => ArtworkDirectoryLookup.FindFirst(["folder"], "p", "id", _ => throw error));
        Assert.Same(error, actual);
    }

    [Fact]
    public void EnumerationExceptionPropagatesUnchanged()
    {
        var error = new IOException("enumerate");
        IEnumerable<string> Candidates()
        {
            yield return "unknown";
            throw error;
        }
        Assert.Same(error, Record.Exception(() => ArtworkDirectoryLookup.FindFirst(Candidates(), "p", "id", _ => null)));
    }
}

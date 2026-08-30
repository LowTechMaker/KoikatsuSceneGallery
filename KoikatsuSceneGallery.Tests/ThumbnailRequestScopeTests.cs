using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class ThumbnailRequestScopeTests
{
    [Fact]
    public void ActivateAfterCancelCreatesFreshToken()
    {
        using var scope = new ThumbnailRequestScope();
        scope.Activate();
        var firstToken = scope.Token;

        scope.Cancel();
        Assert.True(firstToken.IsCancellationRequested);

        scope.Activate();
        Assert.False(scope.Token.IsCancellationRequested);
    }

    [Fact]
    public void CancelingOneScopeDoesNotCancelAnother()
    {
        using var first = new ThumbnailRequestScope();
        using var second = new ThumbnailRequestScope();
        first.Activate();
        second.Activate();

        first.Cancel();

        Assert.True(first.Token.IsCancellationRequested);
        Assert.False(second.Token.IsCancellationRequested);
    }

    [Fact]
    public void DuplicateRequestIsSuppressedUntilFailedRequestCompletes()
    {
        using var scope = new ThumbnailRequestScope();
        scope.Activate();

        Assert.True(scope.TryBegin(@"C:\cards\scene.png"));
        Assert.False(scope.TryBegin(@"c:\CARDS\SCENE.PNG"));

        scope.Complete(@"C:\cards\scene.png", succeeded: false);

        Assert.True(scope.TryBegin(@"C:\cards\scene.png"));
    }

    [Fact]
    public void SamePathWithDifferentSnapshotCanStartAnotherRequest()
    {
        using var scope = new ThumbnailRequestScope();
        scope.Activate();

        Assert.True(scope.TryBegin("C:\\cards\\scene.png\0" + "100\0" + "1"));
        Assert.True(scope.TryBegin("c:\\CARDS\\SCENE.PNG\0" + "120\0" + "2"));
    }

    [Fact]
    public void CanceledScopeRejectsNewRequests()
    {
        using var scope = new ThumbnailRequestScope();
        scope.Activate();
        scope.Cancel();

        Assert.False(scope.TryBegin("scene.png"));
    }
}

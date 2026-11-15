using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class ImportPagePolicyTests
{
    [Theory]
    [InlineData(false, false, false, ImportStep.Add)]
    [InlineData(false, true, false, ImportStep.Review)]
    [InlineData(true, false, false, ImportStep.Review)]
    [InlineData(true, true, false, ImportStep.Review)]
    [InlineData(true, false, true, ImportStep.Import)]
    [InlineData(false, false, true, ImportStep.Import)]
    public void StepFollowsWhatThePageIsDoing(bool hasItems, bool analyzing, bool importing, ImportStep expected)
    {
        Assert.Equal(expected, ImportPagePolicy.CurrentStep(hasItems, analyzing, importing));
    }

    [Fact]
    public void MessagesComeMostUrgentFirst()
    {
        Assert.Equal(
            [ImportMessageKind.Failure, ImportMessageKind.Rejected, ImportMessageKind.Result],
            ImportPagePolicy.OpenMessages(failure: true, rejected: true, result: true));
        Assert.Equal(
            [ImportMessageKind.Rejected, ImportMessageKind.Result],
            ImportPagePolicy.OpenMessages(failure: false, rejected: true, result: true));
        Assert.Empty(ImportPagePolicy.OpenMessages(false, false, false));
    }
}

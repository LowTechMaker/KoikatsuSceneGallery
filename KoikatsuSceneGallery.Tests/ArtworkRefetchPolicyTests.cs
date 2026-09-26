using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public class ArtworkRefetchPolicyTests
{
    private static readonly DateTimeOffset RunStarted = new(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ARunFromTheStartFetchesEverythingIncludingWhatIsAlreadySaved()
    {
        // The whole point of the feature: old data is fetched again too.
        Assert.True(ArtworkRefetchPolicy.NeedsFetch(RunStarted.AddYears(1), resumeFrom: null));
        Assert.True(ArtworkRefetchPolicy.NeedsFetch(null, resumeFrom: null));
    }

    [Fact]
    public void AResumedRunSkipsOnlyWhatThatRunAlreadyFetched()
    {
        Assert.False(ArtworkRefetchPolicy.NeedsFetch(RunStarted.AddMinutes(5), RunStarted));
        Assert.False(ArtworkRefetchPolicy.NeedsFetch(RunStarted, RunStarted));
        // Saved before the run began: an import, or an earlier run.
        Assert.True(ArtworkRefetchPolicy.NeedsFetch(RunStarted.AddSeconds(-1), RunStarted));
    }

    [Fact]
    public void AnArtworkWithNoSavedDataIsAlwaysFetched()
        => Assert.True(ArtworkRefetchPolicy.NeedsFetch(null, RunStarted));

    [Fact]
    public void AMissingOnlyRunLeavesEverySavedArtworkAlone()
    {
        // However old the saved data is: that is the whole point of the mode.
        Assert.False(ArtworkRefetchPolicy.NeedsFetch(RunStarted.AddYears(-1), null, ArtworkRefetchScope.MissingOnly));
        Assert.True(ArtworkRefetchPolicy.NeedsFetch(null, null, ArtworkRefetchScope.MissingOnly));
    }

    [Fact]
    public void AResumedMissingOnlyRunSkipsWhatItAlreadyFetched()
        => Assert.False(ArtworkRefetchPolicy.NeedsFetch(RunStarted.AddMinutes(5), RunStarted, ArtworkRefetchScope.MissingOnly));

    [Fact]
    public void TheEstimateSpansTheLimitersFastestAndSlowestPace()
    {
        var (min, max) = ArtworkRefetchPolicy.EstimatedHours(3600);
        Assert.Equal(2, min);
        Assert.Equal(5, max);
    }
}

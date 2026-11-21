using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public class ImportReviewPolicyTests
{
    [Fact]
    public void UnavailablePostHasItsOwnSectionWithoutClaimingDeletion()
    {
        Assert.Equal(Helpers.ImportReviewSection.Unavailable,
            ImportReviewPolicy.Section(ImportItemStatus.ReadyToImport, false, true));
        Assert.Equal(Helpers.ImportReviewSection.Unidentified,
            ImportReviewPolicy.Section(ImportItemStatus.ReadyToImport, false, false));
        Assert.Equal(Helpers.ImportReviewSection.Unidentified,
            ImportReviewPolicy.Section(ImportItemStatus.Analyzing, false, true));
        Assert.Equal(Helpers.ImportReviewSection.Identified,
            ImportReviewPolicy.Section(ImportItemStatus.ReadyToImport, true, true));
    }
    [Fact]
    public void IdentifiedWithoutDestinationStaysInIdentifiedSectionButCannotImport()
    {
        Assert.True(ImportReviewPolicy.IsIdentified(ImportItemStatus.ReadyToImport, true));
        Assert.False(ImportReviewPolicy.CanExecute(ImportItemStatus.ReadyToImport, null));
    }

    [Fact]
    public void CardNamedByItsFileWithAFoundPostSkipsReview()
        => Assert.True(ImportReviewPolicy.CanAutoImport(
            ImportItemStatus.ReadyToImport, @"C:\lib\a.png", idFromFileName: true, postFound: true, hasAuthor: true));

    [Theory]
    [InlineData(false, true, true, ImportItemStatus.ReadyToImport, @"C:\lib\a.png")] // id not from the file name
    [InlineData(true, false, true, ImportItemStatus.ReadyToImport, @"C:\lib\a.png")] // post not found
    [InlineData(true, true, false, ImportItemStatus.ReadyToImport, @"C:\lib\a.png")] // no author
    [InlineData(true, true, true, ImportItemStatus.AlreadyInLibrary, @"C:\lib\a.png")]
    [InlineData(true, true, true, ImportItemStatus.ReadyToImport, null)] // no destination
    public void AnyMissingPieceLeavesTheCardForReview(
        bool idFromFileName, bool postFound, bool hasAuthor, ImportItemStatus status, string? destination)
        => Assert.False(ImportReviewPolicy.CanAutoImport(status, destination, idFromFileName, postFound, hasAuthor));

    [Fact]
    public void FailedMetadataLookupWithoutAuthorStaysUnidentified()
        => Assert.False(ImportReviewPolicy.IsIdentified(ImportItemStatus.Failed, false));

    [Theory]
    [InlineData(ImportItemStatus.Pending)]
    [InlineData(ImportItemStatus.Analyzing)]
    [InlineData(ImportItemStatus.Failed)]
    public void IncompleteOrFailedItemNeedsAttentionEvenWithAuthor(ImportItemStatus status)
        => Assert.False(ImportReviewPolicy.IsIdentified(status, true));

    [Fact]
    public void ExistingFileDoesNotNeedManualIdentification()
        => Assert.True(ImportReviewPolicy.IsIdentified(ImportItemStatus.AlreadyInLibrary, false));

    [Theory]
    [InlineData(ImportItemStatus.Pending)]
    [InlineData(ImportItemStatus.Analyzing)]
    [InlineData(ImportItemStatus.AlreadyInLibrary)]
    [InlineData(ImportItemStatus.Importing)]
    [InlineData(ImportItemStatus.Completed)]
    [InlineData(ImportItemStatus.Failed)]
    [InlineData(ImportItemStatus.Skipped)]
    public void ShowingAllStatusesDoesNotMakeThemExecutable(ImportItemStatus status)
        => Assert.False(ImportReviewPolicy.CanExecute(status, "C:/library/card.png"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ReadyWithoutDestinationIsExcludedFromConfirmation(string? destination)
        => Assert.False(ImportReviewPolicy.CanExecute(ImportItemStatus.ReadyToImport, destination));

    [Fact] public void ManualIdentificationMovesAnUnknownCardIntoReview()
    {
        Assert.Equal("NeedsInfo", ImportReviewPolicy.Category(ImportItemStatus.Skipped, null, false, false));
        Assert.Equal("Ready", ImportReviewPolicy.Category(ImportItemStatus.ReadyToImport, "C:/library/card.png", true, false));
        Assert.True(ImportReviewPolicy.CanExecute(ImportItemStatus.ReadyToImport, "C:/library/card.png"));
    }
    [Fact] public void ExistingStatusWinsOverMissingMetadata()
        => Assert.Equal("Existing", ImportReviewPolicy.Category(ImportItemStatus.AlreadyInLibrary, "C:/library/card.png", false, true));
    [Fact] public void FailedLookupRemainsVisibleForManualRepair()
        => Assert.Equal("Failed", ImportReviewPolicy.Category(ImportItemStatus.ReadyToImport, "C:/unknown/card.png", false, true));
}

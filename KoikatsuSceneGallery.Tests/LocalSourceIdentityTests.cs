using KoikatsuSceneGallery.Helpers;
using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class LocalSourceIdentityTests
{
    private static readonly ImportPathOptions Options = new("Organized", "{name} ({id})", "{title} ({id})");

    [Fact]
    public void NewId_IsWellFormedAndPrefixed()
    {
        var id = LocalSourceIdentity.NewId();

        Assert.StartsWith(LocalSourceIdentity.FolderIdPrefix, id, StringComparison.Ordinal);
        Assert.True(LocalSourceIdentity.IsValidId(id));
    }

    [Fact]
    public void NewId_DoesNotRepeatAcrossManyDraws()
        => Assert.True(
            Enumerable.Range(0, 200).Select(_ => LocalSourceIdentity.NewId()).Distinct().Count() > 190);

    [Theory]
    [InlineData("阿明")]
    [InlineData("A friend")]
    [InlineData("名前:*?<>|/\\\"")]
    [InlineData("trailing space ")]
    public void FolderRoundTrip_SurvivesSanitizationForAnyDisplayName(string displayName)
    {
        var id = LocalSourceIdentity.NewId();
        var folder = ImportDestinationPolicy.FormatAuthorFolder(Options, displayName, id);

        Assert.True(LocalSourceIdentity.TryParseFolderId(folder, out var parsedId, out var parsedName));
        Assert.Equal(id, parsedId);
        Assert.Equal(PathSanitizer.SanitizeFolderName(displayName).Trim(), parsedName);
    }

    [Fact]
    public void FolderRoundTrip_AcceptsAnEmptyDisplayName()
    {
        Assert.True(LocalSourceIdentity.TryParseFolderId("(local-k7f3q9)", out var id, out var name));
        Assert.Equal("local-k7f3q9", id);
        Assert.Equal("", name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("阿明")]
    [InlineData("作者 (12345678)")]           // a numeric provider id
    [InlineData("作者 (local)")]              // prefix without a slug
    [InlineData("作者 (local-ab)")]           // slug below the minimum length
    [InlineData("作者 (LOCAL-k7f3q9)")]       // the prefix is case-sensitive in folder names
    [InlineData("作者 (local-k7f3q9) extra")] // the marker must close the name
    [InlineData("作者 (local-k7f 3q9)")]      // whitespace is not an id character
    [InlineData("作者 local-k7f3q9")]         // parentheses are required
    public void TryParseFolderId_RejectsAnythingButTheStrictMarker(string folderName)
    {
        Assert.False(LocalSourceIdentity.TryParseFolderId(folderName, out var id, out var name));
        Assert.Equal("", id);
        Assert.Equal("", name);
    }

    [Fact]
    public void TryParseFolderId_RejectsAnOverlongSlug()
        => Assert.False(LocalSourceIdentity.TryParseFolderId(
            $"作者 (local-{new string('a', 33)})", out _, out _));

    // AuthorInfoService tries every provider when a directory sits outside all
    // provider scopes, so the two patterns must stay mutually exclusive.
    [Fact]
    public void LocalFolder_IsNotClaimedByANumericIdProvider()
    {
        var folder = ImportDestinationPolicy.FormatAuthorFolder(Options, "阿明", LocalSourceIdentity.NewId());

        Assert.Null(FakeNumericProvider.TryParseFolderName(folder));
    }

    [Fact]
    public void NumericProviderFolder_IsNotClaimedAsLocal()
    {
        var folder = ImportDestinationPolicy.FormatAuthorFolder(Options, "someone", "12345678");

        Assert.NotNull(FakeNumericProvider.TryParseFolderName(folder));
        Assert.False(LocalSourceIdentity.TryParseFolderId(folder, out _, out _));
    }

    [Theory]
    [InlineData("local", true)]
    [InlineData("LOCAL", true)]
    [InlineData("pixiv", false)]
    [InlineData("locally", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsLocal_MatchesTheProviderIdCaseInsensitively(string? providerId, bool expected)
        => Assert.Equal(expected, LocalSourceIdentity.IsLocal(providerId));

    [Theory]
    [InlineData("k7f3q9", "local-k7f3q9")]
    [InlineData("local-k7f3q9", "local-k7f3q9")]
    [InlineData("  k7f3q9  ", "local-k7f3q9")]
    [InlineData("ab", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("has space", null)]
    public void FormatFolderId_NormalizesOrRejects(string? slug, string? expected)
        => Assert.Equal(expected, LocalSourceIdentity.FormatFolderId(slug));

    /// <summary>
    /// Mirrors the permissive folder parsing a numeric-id provider plugin uses.
    /// </summary>
    private static class FakeNumericProvider
    {
        public static string? TryParseFolderName(string folderName)
        {
            var open = folderName.LastIndexOf('(');
            if (open < 0 || !folderName.EndsWith(')'))
                return null;

            var id = folderName[(open + 1)..^1];
            return id.Length > 0 && id.All(char.IsAsciiDigit) ? id : null;
        }
    }
}

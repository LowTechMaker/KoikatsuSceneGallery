using KoikatsuSceneGallery.Models;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.Tests;

public sealed class CharacterVersionRulesTests
{
    private static readonly DateTime Older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CompareVersions_PutsNewestCurrentFirst()
    {
        var group = new List<(CharacterVariantKind Kind, DateTime Timestamp)>
        {
            (CharacterVariantKind.OldVersion, Newer),
            (CharacterVariantKind.Current, Older),
            (CharacterVariantKind.Current, Newer),
            (CharacterVariantKind.OldVersion, Older),
        };

        group.Sort((a, b) => CharacterVersionRules.CompareVersions(
            a.Kind,
            a.Timestamp,
            b.Kind,
            b.Timestamp));

        Assert.Equal(
            [
                (CharacterVariantKind.Current, Newer),
                (CharacterVariantKind.Current, Older),
                (CharacterVariantKind.OldVersion, Newer),
                (CharacterVariantKind.OldVersion, Older),
            ],
            group);
    }

    [Fact]
    public void CompareVersions_PutsNewestOldVersionFirstWhenNoCurrentExists()
    {
        var group = new List<(CharacterVariantKind Kind, DateTime Timestamp)>
        {
            (CharacterVariantKind.OldVersion, Older),
            (CharacterVariantKind.OldVersion, Newer),
        };

        group.Sort((a, b) => CharacterVersionRules.CompareVersions(
            a.Kind,
            a.Timestamp,
            b.Kind,
            b.Timestamp));

        Assert.Equal(Newer, group[0].Timestamp);
    }

    [Fact]
    public void IsVisible_ShowsOnlyTheLatestCurrentVersionWithoutSearch()
    {
        Assert.True(Visible(
            CharacterVariantKind.Current,
            isLatestVersion: true,
            isGroupRepresentative: true,
            hasSearchQuery: false));
        Assert.False(Visible(
            CharacterVariantKind.Current,
            isLatestVersion: false,
            isGroupRepresentative: false,
            hasSearchQuery: false));
    }

    [Fact]
    public void IsVisible_ShowsGroupRepresentativeWhenCharacterHasNoCurrentVersion()
    {
        // Every copy sits under #old_version, so no card is the latest
        // version. The representative must still reach the gallery.
        Assert.True(Visible(
            CharacterVariantKind.OldVersion,
            isLatestVersion: false,
            isGroupRepresentative: true,
            hasSearchQuery: false));
        Assert.False(Visible(
            CharacterVariantKind.OldVersion,
            isLatestVersion: false,
            isGroupRepresentative: false,
            hasSearchQuery: false));
    }

    [Fact]
    public void IsVisible_KeepsRepresentativeVisibleWhenOldVersionsAreExcludedFromSearch()
    {
        Assert.True(Visible(
            CharacterVariantKind.OldVersion,
            isLatestVersion: false,
            isGroupRepresentative: true,
            hasSearchQuery: true,
            includeOldVersions: false));
    }

    [Fact]
    public void IsVisible_HonorsSearchScopeSettings()
    {
        Assert.False(Visible(
            CharacterVariantKind.OldVersion,
            isLatestVersion: false,
            isGroupRepresentative: false,
            hasSearchQuery: true,
            includeOldVersions: false));
        Assert.True(Visible(
            CharacterVariantKind.OldVersion,
            isLatestVersion: false,
            isGroupRepresentative: false,
            hasSearchQuery: true,
            includeOldVersions: true));

        Assert.False(Visible(
            CharacterVariantKind.Alternative,
            isLatestVersion: true,
            isGroupRepresentative: false,
            hasSearchQuery: true,
            includeAlternatives: false));
        Assert.True(Visible(
            CharacterVariantKind.Alternative,
            isLatestVersion: true,
            isGroupRepresentative: false,
            hasSearchQuery: true,
            includeAlternatives: true));
    }

    [Fact]
    public void IsVisible_HidesAlternativesWithoutSearch()
    {
        Assert.False(Visible(
            CharacterVariantKind.Alternative,
            isLatestVersion: true,
            isGroupRepresentative: true,
            hasSearchQuery: false,
            includeAlternatives: true));
    }

    private static bool Visible(
        CharacterVariantKind kind,
        bool isLatestVersion,
        bool isGroupRepresentative,
        bool hasSearchQuery,
        bool includeAlternatives = false,
        bool includeOldVersions = false)
        => CharacterVersionRules.IsVisible(
            kind,
            isLatestVersion,
            isGroupRepresentative,
            hasSearchQuery,
            includeAlternatives,
            includeOldVersions);
}

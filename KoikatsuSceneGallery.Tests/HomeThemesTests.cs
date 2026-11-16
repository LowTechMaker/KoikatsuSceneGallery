using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class HomeThemesTests
{
    private static readonly DateOnly Today = new(2026, 11, 17);
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static ThemeCard Card(string path, string? author = null, string? post = null, string[]? tags = null,
        bool used = false, DateTimeOffset? lastUsed = null, bool liked = false, bool hidden = false)
        => new(path, author, author is null ? null : author + " name", post ?? "post:" + path, tags ?? [],
            used || lastUsed is not null, lastUsed, liked, hidden, new DateTime(2026, 1, 1));

    private static IEnumerable<ThemeCard> Many(string prefix, int count, Func<int, ThemeCard> make)
        => Enumerable.Range(0, count).Select(make);

    private static IReadOnlyList<ThemeShelf> Pick(IReadOnlyList<ThemeCard> cards, int reroll = 0, int count = 3,
        Func<string, ulong?>? fingerprint = null, DateOnly? day = null)
        => HomeThemes.Pick(cards, day ?? Today, reroll, count, fingerprint);

    /// <summary>A library with every kind available: three authors, a tag, unplayed and dusty favourites.</summary>
    private static List<ThemeCard> Rich()
    {
        var cards = new List<ThemeCard>();
        foreach (var author in new[] { "A", "B", "C" })
            cards.AddRange(Many(author, 8, i => Card($"{author}{i}", author, tags: i < 3 ? ["海"] : ["教室"])));
        cards.AddRange(Many("u", 8, i => Card($"u{i}")));
        cards.AddRange(Many("d", 7, i => Card($"d{i}", liked: true, lastUsed: Now.AddDays(-60))));
        cards.AddRange(Many("p", 30, i => Card($"p{i}", lastUsed: Now.AddDays(-i))));
        return cards;
    }

    // ---------------------------------------------------------------- basics

    [Fact]
    public void AnEmptyOrTinyLibraryGivesNoShelvesAndDoesNotThrow()
    {
        Assert.Empty(Pick([]));
        Assert.Empty(Pick([Card("a"), Card("b", "A")]));
        Assert.Empty(Pick(Rich(), count: 0));
    }

    [Fact]
    public void TheSameDayRerollAndLibraryGiveTheSameShelves()
    {
        var first = Pick(Rich());
        var second = Pick(Rich());

        Assert.Equal(first.Select(s => (s.Kind, s.Subject)), second.Select(s => (s.Kind, s.Subject)));
        Assert.Equal(first.SelectMany(s => s.CardPaths), second.SelectMany(s => s.CardPaths));
    }

    [Fact]
    public void ARerollGivesOtherShelvesOnTheSameDay()
    {
        var baseline = Describe(Pick(Rich()));

        Assert.Contains(Enumerable.Range(1, 10).Select(r => Describe(Pick(Rich(), reroll: r))), other => other != baseline);
    }

    [Fact]
    public void AnotherDayGivesOtherShelves()
    {
        var baseline = Describe(Pick(Rich()));

        Assert.Contains(Enumerable.Range(1, 10).Select(d => Describe(Pick(Rich(), day: Today.AddDays(d)))), other => other != baseline);
    }

    [Fact]
    public void TheOrderTheCardsArriveInDoesNotMatter()
    {
        // The app hands the cards over in scan order, which changes from launch
        // to launch. Posts shared between cards make the in-shelf spreading
        // break ties, which is where an order dependence would show.
        var cards = Rich();
        cards.AddRange(Many("T", 12, i => Card($"T{i:D2}", i < 6 ? "T" : "S", post: $"post:t{i % 4}", tags: ["山"])));
        cards.AddRange(Many("q", 9, i => Card($"q{i}", "Q", post: $"post:q{i % 3}", liked: true)));

        for (var day = 0; day < 15; day++)
            for (var reroll = 0; reroll < 3; reroll++)
            {
                var expected = Describe(Pick(cards, reroll, day: Today.AddDays(day)));
                var reversed = Enumerable.Reverse(cards).ToList();
                var shuffled = cards.OrderBy(_ => Random.Shared.Next()).ToList();

                Assert.Equal(expected, Describe(Pick(reversed, reroll, day: Today.AddDays(day))));
                Assert.Equal(expected, Describe(Pick(shuffled, reroll, day: Today.AddDays(day))));
            }
    }

    [Fact]
    public void AnAuthorWithFewerThanSixCardsIsNotATheme()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("Small", 5, i => Card($"Small{i}", "Small", lastUsed: Now.AddDays(-1))));
        cards.AddRange(Many("Big", 8, i => Card($"Big{i}", "Big", lastUsed: Now.AddDays(-1))));

        for (var day = 0; day < 20; day++)
            Assert.DoesNotContain(Pick(cards, day: Today.AddDays(day), count: 5), s => s.SubjectKey == "Small");
    }

    private static string Describe(IReadOnlyList<ThemeShelf> shelves)
        => string.Join(";", shelves.Select(s => $"{s.Kind}:{s.Subject}:{string.Join(",", s.CardPaths)}"));

    [Fact]
    public void ThreeShelvesAreChosenFromARichLibrary()
        => Assert.Equal(3, Pick(Rich()).Count);

    [Fact]
    public void FewerCandidatesGiveFewerShelves()
    {
        // Only "never played" qualifies (half the library, no authors, tags or likes).
        var shelves = Pick([.. Many("u", 8, i => Card($"u{i}")), .. Many("p", 8, i => Card($"p{i}", lastUsed: Now.AddDays(-1)))]);

        Assert.Equal(ThemeKind.NeverPlayed, Assert.Single(shelves).Kind);
    }

    // ---------------------------------------------------------------- the 6-card rule and hidden cards

    [Fact]
    public void AThemeNeedsSixCardsAfterHiddenOnesAreRemoved()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("u", 6, i => Card($"u{i}", hidden: i == 0)));
        // Played cards, so "never played" is not most of the library.
        cards.AddRange(Many("p", 6, i => Card($"p{i}", lastUsed: Now.AddDays(-1))));

        Assert.Empty(Pick(cards));

        cards.Add(Card("u6"));
        var shelf = Assert.Single(Pick(cards));
        Assert.DoesNotContain("u0", shelf.CardPaths);
    }

    // ---------------------------------------------------------------- kinds

    [Fact]
    public void AtMostOneShelfPerKindButTwoForAuthorsAndTags()
    {
        for (var day = 0; day < 40; day++)
        {
            var shelves = Pick(Rich(), count: 5, day: Today.AddDays(day));
            foreach (var group in shelves.GroupBy(s => s.Kind))
                Assert.True(group.Count() <= (group.Key is ThemeKind.Author or ThemeKind.Tag ? 2 : 1), $"day {day}: {group.Key}");
            foreach (var group in shelves.GroupBy(s => (s.Kind, s.Subject)))
                Assert.Single(group);
        }
    }

    [Fact]
    public void NeverThreeAuthorOrTagShelvesWhenAnotherKindIsAvailable()
    {
        // Two authors and two tags are always candidates, and one other kind
        // (never played): four author/tag shelves to choose three from.
        var cards = new List<ThemeCard>();
        foreach (var author in new[] { "A", "B", "C", "D", "E" })
            cards.AddRange(Many(author, 8, i => Card($"{author}{i}", author, lastUsed: Now.AddDays(-1),
                tags: i < 3 ? ["海"] : i < 6 ? ["山"] : [])));
        cards.AddRange(Many("u", 8, i => Card($"u{i}")));

        for (var day = 0; day < 60; day++)
        {
            var shelves = Pick(cards, day: Today.AddDays(day));
            Assert.Equal(3, shelves.Count);
            Assert.Contains(shelves, s => s.Kind is not (ThemeKind.Author or ThemeKind.Tag));
        }
    }

    [Fact]
    public void ThreeAuthorOrTagShelvesAreFineWhenNothingElseIsAvailable()
    {
        // Everything played and nothing liked: only authors (at most two) and a tag.
        var cards = new List<ThemeCard>();
        foreach (var author in new[] { "A", "B", "C" })
            cards.AddRange(Many(author, 8, i => Card($"{author}{i}", author, lastUsed: Now.AddDays(-1),
                tags: i < 2 ? ["海"] : [])));

        var shelves = Pick(cards);

        Assert.Equal(3, shelves.Count);
        Assert.All(shelves, s => Assert.True(s.Kind is ThemeKind.Author or ThemeKind.Tag));
    }

    [Fact]
    public void AnAuthorShelfIsOneAuthorsCards()
    {
        foreach (var shelf in Pick(Rich(), count: 5).Where(s => s.Kind == ThemeKind.Author))
        {
            Assert.All(shelf.CardPaths, path => Assert.StartsWith(shelf.SubjectKey!, path));
            Assert.Equal(shelf.SubjectKey + " name", shelf.Subject);
        }
    }

    [Fact]
    public void AHugeAuthorDoesNotWinEveryDay()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("Big", 200, i => Card($"Big{i}", "Big")));
        foreach (var author in new[] { "S1", "S2", "S3", "S4", "S5" })
            cards.AddRange(Many(author, 10, i => Card($"{author}{i}", author)));

        // Two authors are drawn a day. Weighted by size, the big one would be on
        // nearly every day; by the square root of size, on about three in four.
        var days = Enumerable.Range(0, 200)
            .Select(d => Pick(cards, day: Today.AddDays(d), count: 5).Where(s => s.Kind == ThemeKind.Author)
                .Select(s => s.SubjectKey).ToList())
            .ToList();
        var bigDays = days.Count(authors => authors.Contains("Big"));

        Assert.True(bigDays < days.Count * 0.9, $"the big author on {bigDays} of {days.Count} days");
        Assert.True(days.SelectMany(a => a).Distinct().Count() >= 5);
    }

    [Fact]
    public void AuthorsWithUnplayedCardsArePreferred()
    {
        // Six equal authors, three never played and three all played: two are
        // drawn each day, and the fresh ones should be drawn far more often.
        var cards = new List<ThemeCard>();
        foreach (var author in new[] { "P1", "P2", "P3" })
            cards.AddRange(Many(author, 10, i => Card($"{author}-{i}", author, lastUsed: Now.AddDays(-1))));
        foreach (var author in new[] { "F1", "F2", "F3" })
            cards.AddRange(Many(author, 10, i => Card($"{author}-{i}", author)));
        // Plenty of other unplayed cards, so "never played" does not repeat a fresh author's shelf.
        cards.AddRange(Many("u", 200, i => Card($"u{i}")));

        var authors = Enumerable.Range(0, 200)
            .SelectMany(d => Pick(cards, day: Today.AddDays(d), count: 5).Where(s => s.Kind == ThemeKind.Author))
            .Select(s => s.SubjectKey!)
            .ToList();
        var fresh = authors.Count(a => a.StartsWith('F'));
        var played = authors.Count(a => a.StartsWith('P'));

        Assert.True(fresh > played * 2, $"fresh {fresh}, played {played}");
    }

    [Fact]
    public void ATagThatCoversMoreThanAThirdOfTheLibrarySaysNothing()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("x", 30, i => Card($"x{i}", $"author{i % 10}", lastUsed: Now.AddDays(-1),
            tags: i < 9 ? ["everywhere", "sometimes"] : ["everywhere"])));

        var tags = Enumerable.Range(0, 30).SelectMany(d => Pick(cards, day: Today.AddDays(d), count: 5))
            .Where(s => s.Kind == ThemeKind.Tag).Select(s => s.Subject).Distinct().ToList();

        Assert.DoesNotContain("everywhere", tags);
        Assert.Contains("sometimes", tags);
    }

    [Fact]
    public void ATagWithFewerThanSixCardsOrOnlyOneAuthorIsNotATheme()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("x", 40, i => Card($"x{i}", $"author{i % 10}", lastUsed: Now.AddDays(-1),
            tags: i < 5 ? ["rare"] : [])));
        cards.AddRange(Many("y", 8, i => Card($"y{i}", "Solo", lastUsed: Now.AddDays(-1), tags: ["solo-tag"])));
        cards.AddRange(Many("z", 8, i => Card($"z{i}", $"mixed{i}", lastUsed: Now.AddDays(-1), tags: ["shared"])));

        var tags = Enumerable.Range(0, 30).SelectMany(d => Pick(cards, day: Today.AddDays(d), count: 5))
            .Where(s => s.Kind == ThemeKind.Tag).Select(s => s.Subject).Distinct().ToList();

        Assert.DoesNotContain("rare", tags);
        Assert.DoesNotContain("solo-tag", tags);
        Assert.Contains("shared", tags);
    }

    [Fact]
    public void DustyFavouritesAreLikedAndUnusedForThirtyDaysOrNever()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("old", 4, i => Card($"old{i}", liked: true, lastUsed: Now.AddDays(-31))));
        cards.AddRange(Many("never", 3, i => Card($"never{i}", liked: true)));
        cards.AddRange(Many("recent", 5, i => Card($"recent{i}", liked: true, lastUsed: Now.AddDays(-29))));
        cards.AddRange(Many("plain", 5, i => Card($"plain{i}", lastUsed: Now.AddDays(-90))));

        var dusty = Pick(cards, count: 5).Single(s => s.Kind == ThemeKind.DustyFavourites);

        Assert.Equal(7, dusty.CardPaths.Count);
        Assert.All(dusty.CardPaths, p => Assert.True(p.StartsWith("old") || p.StartsWith("never")));
    }

    [Fact]
    public void NeverPlayedIsCardsNeverUsed()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("u", 7, i => Card($"u{i}")));
        cards.AddRange(Many("p", 7, i => Card($"p{i}", lastUsed: Now.AddDays(-1))));

        var shelf = Pick(cards, count: 5).Single(s => s.Kind == ThemeKind.NeverPlayed);

        Assert.All(shelf.CardPaths, p => Assert.StartsWith("u", p));
    }

    [Fact]
    public void NeverPlayedIsOfferedOnlyWhenAtMostHalfTheLibraryIsUnplayed()
    {
        // Exactly half unplayed: offered (nothing else qualifies, so it is the shelf).
        List<ThemeCard> half = [.. Many("u", 7, i => Card($"u{i}")), .. Many("p", 7, i => Card($"p{i}", lastUsed: Now.AddDays(-1)))];
        Assert.Equal(ThemeKind.NeverPlayed, Assert.Single(Pick(half)).Kind);

        // One played card fewer: more than half unplayed, so it says nothing, on any day.
        List<ThemeCard> most = [.. Many("u", 7, i => Card($"u{i}")), .. Many("p", 6, i => Card($"p{i}", lastUsed: Now.AddDays(-1)))];
        for (var day = 0; day < 20; day++)
            Assert.Empty(Pick(most, day: Today.AddDays(day), count: 5));

        // Hidden cards do not count on either side.
        List<ThemeCard> hiddenPlayed = [.. half.Take(7), .. half.Skip(7).Select((c, i) => i == 0 ? c with { Hidden = true } : c)];
        Assert.Empty(Pick(hiddenPlayed, count: 5));
    }

    // ---------------------------------------------------------------- inside a shelf

    [Fact]
    public void InsideAShelfUnplayedCardsComeFirstAndThereAreAtMostThirty()
    {
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("A", 40, i => Card($"A{i:D2}", "A", lastUsed: i < 30 ? Now.AddDays(-1) : null)));

        var shelf = Pick(cards, count: 5).Single(s => s.Kind == ThemeKind.Author);

        Assert.Equal(30, shelf.CardPaths.Count);
        var unplayed = Enumerable.Range(30, 10).Select(i => $"A{i:D2}").ToHashSet();
        Assert.All(shelf.CardPaths.Take(10), p => Assert.Contains(p, unplayed));
    }

    [Fact]
    public void NoTwoNeighboursShareAPostWhenThatCanBeAvoided()
    {
        // Four posts of three cards each, all never played.
        var cards = Enumerable.Range(0, 12).Select(i => Card($"c{i:D2}", "A", post: $"post:{i % 4}")).ToList();

        for (var day = 0; day < 30; day++)
        {
            // The author shelf and "never played" hold the same twelve; one is chosen.
            var shelf = Pick(cards, day: Today.AddDays(day), count: 5)[0];
            var posts = shelf.CardPaths.Select(p => cards.Single(c => c.Path == p).PostKey).ToList();
            for (var i = 1; i < posts.Count; i++)
                Assert.NotEqual(posts[i - 1], posts[i]);
        }
    }

    // ---------------------------------------------------------------- overlap

    [Fact]
    public void TwoShelvesSharingMoreThanHalfTheirCardsAreNotBothChosen()
    {
        // The tag is exactly author A's unplayed cards plus one of B's: as a shelf
        // it would repeat A's shelf.
        var cards = new List<ThemeCard>();
        cards.AddRange(Many("A", 8, i => Card($"A{i}", "A", tags: ["海"])));
        cards.Add(Card("B0", "B", tags: ["海"]));
        cards.AddRange(Many("B", 7, i => Card($"B{i + 1}", "B", lastUsed: Now.AddDays(-1))));
        cards.AddRange(Many("q", 30, i => Card($"q{i}", $"other{i}", lastUsed: Now.AddDays(-2))));

        for (var day = 0; day < 40; day++)
        {
            var shelves = Pick(cards, day: Today.AddDays(day), count: 5);
            for (var i = 0; i < shelves.Count; i++)
                for (var j = i + 1; j < shelves.Count; j++)
                {
                    var shared = shelves[i].CardPaths.Intersect(shelves[j].CardPaths).Count();
                    Assert.True(shared * 2 <= Math.Min(shelves[i].CardPaths.Count, shelves[j].CardPaths.Count),
                        $"day {day}: {shelves[i].Kind} {shelves[i].Subject} / {shelves[j].Kind} {shelves[j].Subject}");
                }
        }
    }

    // ---------------------------------------------------------------- similar to the last one

    [Fact]
    public void SimilarToLastNeedsFingerprints()
    {
        var cards = SimilarLibrary();

        Assert.DoesNotContain(Pick(cards, count: 5), s => s.Kind == ThemeKind.SimilarToLast);
        Assert.DoesNotContain(Pick(cards, count: 5, fingerprint: _ => null), s => s.Kind == ThemeKind.SimilarToLast);
    }

    [Fact]
    public void SimilarToLastIsTheCardsThatLookLikeTheMostRecentlyUsedOne()
    {
        var cards = SimilarLibrary();

        var shelf = Pick(cards, count: 5, fingerprint: Fingerprint).Single(s => s.Kind == ThemeKind.SimilarToLast);

        Assert.Equal("last", shelf.Subject);
        Assert.Equal(6, shelf.CardPaths.Count);
        Assert.All(shelf.CardPaths, p => Assert.StartsWith("near", p));
    }

    private static List<ThemeCard> SimilarLibrary()
    {
        var cards = new List<ThemeCard> { Card("last", lastUsed: Now.AddHours(-1)), Card("older", lastUsed: Now.AddDays(-3)) };
        cards.AddRange(Many("near", 6, i => Card($"near{i}", lastUsed: Now.AddDays(-10))));
        cards.AddRange(Many("far", 6, i => Card($"far{i}")));
        return cards;
    }

    // "last" is all zeros; near cards differ in a few bits, far ones in most.
    private static ulong? Fingerprint(string path) => path switch
    {
        "last" => 0UL,
        "older" => ulong.MaxValue,
        _ when path.StartsWith("near") => 0b111UL << int.Parse(path[4..]),
        _ when path.StartsWith("far") => ulong.MaxValue >> int.Parse(path[3..]),
        _ => null,
    };
}

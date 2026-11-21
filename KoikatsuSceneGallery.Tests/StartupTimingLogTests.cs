using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Tests;

public sealed class StartupTimingLogTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 17, 11, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void ALineHoldsTheStartTheLibraryThePhaseTheTimeAndTheCounts()
    {
        var line = StartupTimingLog.Format("s1", At, new("scenes", "scan", 1234, [("listed", 5000), ("opened", 3)]));

        Assert.Equal("2026-11-17T11:00:00+08:00\tstart=s1\tlib=scenes\tphase=scan\tms=1234\tlisted=5000\topened=3", line);
    }

    [Fact]
    public void NamesCannotBreakTheLineOrCarryAPath()
    {
        // Only fixed identifiers are logged; anything else is reduced to letters, digits, dot, dash, underscore.
        var line = StartupTimingLog.Format("s\t1", At, new(@"C:\lib\x", "scan\nevil", 1, [("a b", 1)]));

        Assert.DoesNotContain("\n", line);
        Assert.Equal(5 + 1, line.Split('\t').Length);
        Assert.DoesNotContain(@"\", line);
        Assert.DoesNotContain(":l", line);
    }

    [Fact]
    public void TrimKeepsTheLinesOfTheLastStarts()
    {
        var lines = Enumerable.Range(1, 60).SelectMany(i => new[]
        {
            StartupTimingLog.Format($"s{i}", At, new("scenes", "scan", i, [])),
            StartupTimingLog.Format($"s{i}", At, new("scenes", "load.finished", i, [])),
        }).ToList();

        var kept = StartupTimingLog.Trim(lines, 50);

        Assert.Equal(100, kept.Count);
        Assert.StartsWith(StartupTimingLog.Format("s11", At, new("scenes", "scan", 11, [])), kept[0]);
        Assert.Equal(lines[^1], kept[^1]);
    }

    [Fact]
    public void TrimDropsLinesItCannotRead()
    {
        var kept = StartupTimingLog.Trim(["garbage", "", StartupTimingLog.Format("s1", At, new("scenes", "scan", 1, []))], 50);

        Assert.Single(kept);
    }

    [Fact]
    public void AppendCreatesTheFileAddsTheStartAndKeepsFifty()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, StartupTimingLog.FileName);

        for (var i = 1; i <= 55; i++)
            StartupTimingLog.Append(path, $"s{i}", At, [new("scenes", "scan", i, [("opened", 0)]), new("scenes", "load.finished", i, [])]);

        var lines = File.ReadAllLines(path);
        Assert.Equal(100, lines.Length);
        Assert.Contains("start=s6\t", lines[0]);
        Assert.Contains("start=s55\t", lines[^1]);
    }

    [Fact]
    public void AppendNeverThrows()
    {
        // The log must never stop the app: a path it cannot write is ignored.
        StartupTimingLog.Append(Path.Combine("Z:\\", "no", "such", "\0bad", StartupTimingLog.FileName), "s1", At, [new("scenes", "scan", 1, [])]);
    }
}

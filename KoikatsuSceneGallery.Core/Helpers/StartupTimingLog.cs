using System.Text;

namespace KoikatsuSceneGallery.Helpers;

/// <summary>One measured step of a library's load: how long it took and how much it handled.</summary>
/// <param name="Library">A fixed name: scenes, characters, coordinates, home.</param>
/// <param name="Phase">A fixed name, e.g. cache.read, scan, first.cards, load.finished.</param>
/// <param name="Milliseconds">The step's duration, or for a moment (first.cards, load.finished) the time since the process started.</param>
/// <param name="Counts">Named numbers: cards from cache, files listed, files opened, cards added or removed, sidecars read.</param>
internal sealed record StartupPhase(string Library, string Phase, long Milliseconds, IReadOnlyList<(string Name, long Value)> Counts);

/// <summary>
/// <c>startup_timing.log</c> in the app's data folder: one tab-separated line
/// per phase per library per start, appended and trimmed to the last
/// <see cref="KeepStarts"/> starts. It records names and numbers only, never
/// a path or a file name, so the user's own starts can tell where the time
/// goes without anyone reading their library.
/// </summary>
internal static class StartupTimingLog
{
    public const string FileName = "startup_timing.log";
    public const int KeepStarts = 50;

    public static string Format(string startId, DateTimeOffset at, StartupPhase phase)
    {
        var line = new StringBuilder()
            .Append(at.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture))
            .Append("\tstart=").Append(Clean(startId))
            .Append("\tlib=").Append(Clean(phase.Library))
            .Append("\tphase=").Append(Clean(phase.Phase))
            .Append("\tms=").Append(phase.Milliseconds);
        foreach (var (name, value) in phase.Counts)
            line.Append('\t').Append(Clean(name)).Append('=').Append(value);
        return line.ToString();
    }

    /// <summary>Only letters, digits, dot, dash and underscore: no separator, no line break, no path.</summary>
    private static string Clean(string value)
        => new([.. value.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')]);

    /// <summary>The lines of the last <paramref name="keepStarts"/> starts, in order; unreadable lines are dropped.</summary>
    public static List<string> Trim(IEnumerable<string> lines, int keepStarts)
    {
        var readable = lines.Select(line => (Line: line, Start: StartOf(line))).Where(x => x.Start is not null).ToList();
        var starts = readable.Select(x => x.Start!).Distinct().ToList();
        var kept = starts.Skip(Math.Max(0, starts.Count - keepStarts)).ToHashSet();
        return [.. readable.Where(x => kept.Contains(x.Start!)).Select(x => x.Line)];
    }

    private static string? StartOf(string line)
    {
        var fields = line.Split('\t');
        return fields.Length >= 5 && fields[1].StartsWith("start=", StringComparison.Ordinal) ? fields[1][6..] : null;
    }

    /// <summary>Appends one start's phases and trims the file. Never throws: the log must not stop the app.</summary>
    public static void Append(string path, string startId, DateTimeOffset at, IEnumerable<StartupPhase> phases)
    {
        try
        {
            var existing = File.Exists(path) ? File.ReadAllLines(path) : [];
            var lines = Trim([.. existing, .. phases.Select(phase => Format(startId, at, phase))], KeepStarts);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllLines(temporary, lines);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception)
        {
            // A full disk or a locked file costs this start's numbers, nothing else.
        }
    }
}

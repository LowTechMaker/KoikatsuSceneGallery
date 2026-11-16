using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>What the migration needs to know about the library; built by the app from settings and plugins.</summary>
/// <param name="Roots">Scene, character and coordinate roots together.</param>
/// <param name="ImportSubfolder">The folder under each root the importer fills ("Organized").</param>
/// <param name="ProviderFolders">Every plugin's folder plus the Unknown folder; nothing else is touched.</param>
/// <param name="ParseAuthorId">(provider folder, folder name) → the author id that folder names, or null.</param>
internal sealed record LayoutMigrationOptions(
    IReadOnlyList<string> Roots,
    string ImportSubfolder,
    IReadOnlyList<string> ProviderFolders,
    string UnknownFolderName,
    IReadOnlyList<string> GameFolderNames,
    IReadOnlyList<(string Name, int Rating)> RatingFolderNames,
    Func<string, string, string?> ParseAuthorId);

/// <summary>Why a file stays where it is.</summary>
internal enum LayoutStayReason
{
    /// <summary>A different file already has (or would get) the destination path.</summary>
    Collision,
    /// <summary>An identical file already has (or would get) the destination path.</summary>
    Duplicate,
    /// <summary>A file loose in a provider's game or rating folder, outside any author folder.</summary>
    Loose,
    /// <summary>Its rating could not be recorded first, and it lives in the folder that says it.</summary>
    RatingNotSaved,
    /// <summary>The file changed, moved or disappeared since the plan (or the migration) saw it.</summary>
    Changed,
    /// <summary>The move itself failed; the file is where it was.</summary>
    Failed,
}

/// <summary>One file to move.</summary>
/// <param name="Length">The source's length when planned; a different length at apply time means it changed.</param>
/// <param name="WrittenUtc">The source's last write time when planned, likewise.</param>
internal sealed record LayoutMove(string Root, string Source, string Destination, long Length, DateTime WrittenUtc);

internal sealed record LayoutStay(string Root, string Source, string? Destination, LayoutStayReason Reason, string? Detail = null);

/// <summary>The per-folder documents that are merged rather than moved.</summary>
internal enum LayoutDocumentKind
{
    /// <summary>A post sidecar, <c>.scenegallery\fetched_data\&lt;provider&gt;_&lt;post&gt;.json</c>.</summary>
    Sidecar,
    /// <summary>Character annotations (versions, notes), <c>.scenegallery\characters.json</c>.</summary>
    Characters,
    /// <summary>A local source's identity, <c>.scenegallery\source.json</c>.</summary>
    LocalSource,
}

/// <summary>Documents from one or more old folders that land on one destination document.</summary>
internal sealed record LayoutDocumentMerge(string Root, LayoutDocumentKind Kind, string Destination, IReadOnlyList<string> Sources);

/// <summary>Files the migration saw but will not touch, by where they are.</summary>
internal sealed record LayoutOutOfScope(string Location, int FileCount);

internal sealed record LayoutMigrationPlan(
    IReadOnlyList<string> Roots,
    IReadOnlyList<LayoutMove> Moves,
    IReadOnlyList<LayoutDocumentMerge> Merges,
    IReadOnlyList<LayoutStay> Stays,
    IReadOnlyList<(string Path, int Rating)> Ratings,
    int AuthorsMerged,
    IReadOnlyList<LayoutOutOfScope> OutOfScope,
    IReadOnlyList<string> OldFolders,
    IReadOnlyList<string> UnfinishedJournals)
{
    /// <summary>The plan as plain text, one line per file.</summary>
    public string ToText()
    {
        var text = new StringBuilder();
        foreach (var move in Moves) text.Append(move.Source).Append(" -> ").AppendLine(move.Destination);
        foreach (var merge in Merges)
            foreach (var source in merge.Sources)
                text.Append(source).Append(" -> ").Append(merge.Destination).AppendLine(" (merged)");
        foreach (var stay in Stays)
            text.Append(stay.Source).Append(" stays: ").Append(stay.Reason)
                .AppendLine(stay.Destination is null ? "" : $" ({stay.Destination})");
        foreach (var other in OutOfScope)
            text.Append(other.Location).Append(" not touched: ").Append(other.FileCount).AppendLine(" files");
        return text.ToString();
    }
}

/// <summary>What the migration needs from the app around the file moves.</summary>
/// <param name="RecordRatings">Files each (old path, rating) as the card's filed rating; returns the paths that could not be.</param>
/// <param name="PathsMoved">Re-points everything keyed by path, once per root, with (from, to) pairs.</param>
/// <param name="AfterMove">Test seam: runs after a file moved, before the journal records it.</param>
internal sealed record LayoutMigrationSteps(
    LibraryCardMover Mover,
    Func<IReadOnlyList<(string Path, int Rating)>, CancellationToken, Task<IReadOnlyList<string>>> RecordRatings,
    Func<IReadOnlyList<(string From, string To)>, CancellationToken, Task> PathsMoved,
    Func<DateTimeOffset>? Now = null,
    Action<string>? AfterMove = null);

internal sealed record LayoutMigrationProgress(int Done, int Total);

/// <summary>A run that can be moved back: its journal, root, when it started, and how many files it moved (and still has moved).</summary>
internal sealed record LayoutMigrationRun(string Journal, string Root, DateTimeOffset? At, int Moved);

/// <param name="Stopped">Cancelled between files: what moved stays moved; the run can be resumed or undone.</param>
internal sealed record LayoutMigrationResult(
    int Moved, IReadOnlyList<LayoutStay> Stayed, bool Stopped, IReadOnlyList<string> Journals);

/// <summary>
/// Moves a library from the old five-level layout
/// (<c>provider\game\rating\author\…</c>) to the three-level one
/// (<c>provider\author\…</c>), with a journal that can move everything back.
/// </summary>
/// <remarks>
/// Pure file work and decisions; the app supplies the rating store and the
/// path-keyed stores through <see cref="LayoutMigrationSteps"/>.
///
/// The guards, each tested:
/// <list type="bullet">
/// <item>Only files under <c>&lt;root&gt;\&lt;import folder&gt;\&lt;known provider&gt;</c>
/// are considered, and only the two positions right after the provider folder
/// are ever stripped: a game folder, then a rating folder.</item>
/// <item>Collisions are decided before anything moves, and decided again by
/// <see cref="LibraryCardMover"/> at the time of the move: nothing is ever
/// overwritten, renamed or linked. A file that changed since the plan stays.</item>
/// <item>Every rating an old folder implies is filed before the first file
/// moves; a card whose rating could not be filed does not move.</item>
/// <item>Each move is written ahead to the journal and confirmed after, flushed
/// to disk, so a killed run leaves at most one unconfirmed move, which is
/// recognised by its length and write time on resume and on rollback.</item>
/// <item>Merged documents keep their originals beside the journal; rollback
/// restores them byte for byte.</item>
/// <item>Only folders that are empty are removed, and rollback recreates them.</item>
/// </list>
/// </remarks>
internal static class LibraryLayoutMigration
{
    public const string JournalPrefix = "layout_migration_";
    private const string FetchedData = PostMetadataStore.FetchedDataDirectoryName;
    private const string Metadata = LibraryMetadataDirectory.Name;

    private static readonly JsonSerializerOptions JournalJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions DocumentJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // ---------------------------------------------------------------- planning

    /// <summary>What would move, merge and stay. Reads the file system; changes nothing.</summary>
    public static LayoutMigrationPlan Plan(LayoutMigrationOptions options, CancellationToken ct)
    {
        var games = new HashSet<string>(options.GameFolderNames.Where(n => !string.IsNullOrWhiteSpace(n)), PathComparison.Comparer);
        var ratings = new Dictionary<string, int>(PathComparison.Comparer);
        foreach (var (name, rating) in options.RatingFolderNames)
            if (!string.IsNullOrWhiteSpace(name)) ratings.TryAdd(name, rating);

        var moves = new List<LayoutMove>();
        var merges = new List<LayoutDocumentMerge>();
        var stays = new List<LayoutStay>();
        var filed = new List<(string, int)>();
        var outOfScope = new List<LayoutOutOfScope>();
        var oldFolders = new List<string>();
        var authorsMerged = 0;
        var roots = options.Roots.Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(Path.GetFullPath).Distinct(PathComparison.Comparer).ToList();

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;
            var organized = Path.Combine(root, options.ImportSubfolder.Trim());
            var providerDirs = options.ProviderFolders
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => Path.Combine(organized, p))
                .Distinct(PathComparison.Comparer)
                .ToList();
            outOfScope.AddRange(CountOutOfScope(root, providerDirs, ct));

            foreach (var provider in options.ProviderFolders
                         .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(PathComparison.Comparer))
            {
                // As configured (it can be a relative path), so ParseAuthorId knows which plugin.
                var providerDir = Path.Combine(organized, provider);
                if (!Directory.Exists(providerDir)) continue;
                var isUnknown = string.Equals(provider, options.UnknownFolderName, PathComparison.Comparison);
                var entries = new List<(string Source, string[] Tail, int Rating, string OldFolder)>();
                var existingNew = new Dictionary<string, string>(StringComparer.Ordinal);

                foreach (var file in EnumerateFiles(providerDir, ct))
                {
                    var segments = Path.GetRelativePath(providerDir, file).Split(Path.DirectorySeparatorChar);
                    var skip = 0;
                    var rating = 0;
                    if (segments.Length > 1 && games.Contains(segments[0])) skip = 1;
                    if (segments.Length > skip + 1 && ratings.TryGetValue(segments[skip], out var r))
                    {
                        rating = r;
                        skip++;
                    }
                    if (skip == 0) continue; // Already the new layout.
                    // The old author folder (for a loose file, the file itself; never grouped).
                    entries.Add((file, segments[skip..], rating, Path.Combine([providerDir, .. segments[..(skip + 1)]])));
                }

                // Author folders already in the new layout are merge targets,
                // even when empty. Ordinal order makes a tie deterministic.
                if (!isUnknown)
                {
                    foreach (var folder in Directory.EnumerateDirectories(providerDir).Order(StringComparer.Ordinal))
                    {
                        var name = Path.GetFileName(folder);
                        if (games.Contains(name) || ratings.ContainsKey(name)) continue;
                        if (options.ParseAuthorId(provider, name) is { } id) existingNew.TryAdd(id, name);
                    }
                }

                // The author folder name every old folder of one author goes to.
                var targetName = new Dictionary<string, string>(StringComparer.Ordinal);
                if (!isUnknown)
                {
                    foreach (var group in entries
                                 .Where(e => e.Tail.Length > 1)
                                 .Select(e => (e.Tail[0], Id: options.ParseAuthorId(provider, e.Tail[0]), e.OldFolder))
                                 .Where(e => e.Id is not null)
                                 .GroupBy(e => e.Id!, StringComparer.Ordinal))
                    {
                        var names = group.GroupBy(e => e.Item1, PathComparison.Comparer)
                            .Select(g => (Name: g.Key, Files: g.Count())).ToList();
                        // Merged = more than one old folder, by path: the same name
                        // under KK\R-18G and under KKS\G is two folders becoming one.
                        var oldFolderCount = group.Select(e => e.OldFolder).Distinct(PathComparison.Comparer).Count();
                        if (existingNew.TryGetValue(group.Key, out var existing))
                        {
                            targetName[group.Key] = existing;
                            authorsMerged++;
                        }
                        else
                        {
                            targetName[group.Key] = names
                                .OrderByDescending(n => n.Files)
                                .ThenBy(n => n.Name, StringComparer.Ordinal)
                                .First().Name;
                            if (oldFolderCount > 1) authorsMerged++;
                        }
                    }
                }

                var byDestination = new Dictionary<string, List<(string Source, int Rating)>>(PathComparison.Comparer);
                foreach (var (source, rest, rating, _) in entries)
                {
                    string[] destination;
                    if (isUnknown)
                    {
                        destination = rest.Length == 1
                            ? [GalleryGrouping.UnrecognizedFolderName, rest[0]]
                            : rest;
                    }
                    else if (rest.Length == 1)
                    {
                        stays.Add(new(root, source, null, LayoutStayReason.Loose));
                        continue;
                    }
                    else
                    {
                        destination = [.. rest];
                        if (options.ParseAuthorId(provider, rest[0]) is { } id && targetName.TryGetValue(id, out var name))
                            destination[0] = name;
                    }

                    var path = Path.Combine([providerDir, .. destination]);
                    if (!byDestination.TryGetValue(path, out var sources))
                        byDestination[path] = sources = [];
                    sources.Add((source, rating));
                }

                foreach (var (destination, sources) in byDestination)
                {
                    sources.Sort((a, b) => string.CompareOrdinal(a.Source, b.Source));
                    if (DocumentKindOf(destination) is { } kind)
                    {
                        // Documents speak for files that may not all move, so
                        // they are handled after the moves, even a single one.
                        merges.Add(new(root, kind, destination, [.. sources.Select(s => s.Source)]));
                        continue;
                    }

                    var occupied = File.Exists(destination) || Directory.Exists(destination);
                    string? first = occupied ? destination : null;
                    foreach (var (source, rating) in sources)
                    {
                        if (first is null)
                        {
                            var info = new FileInfo(source);
                            moves.Add(new(root, source, destination, info.Length, info.LastWriteTimeUtc));
                            if (rating > 0 && IsCard(source)) filed.Add((source, rating));
                            first = source;
                            continue;
                        }
                        var same = File.Exists(first) && SameBytes(source, first);
                        stays.Add(new(root, source, destination, same ? LayoutStayReason.Duplicate : LayoutStayReason.Collision));
                    }
                }

                foreach (var folder in EnumerateDirectories(providerDir, ct))
                {
                    var first = Path.GetRelativePath(providerDir, folder).Split(Path.DirectorySeparatorChar)[0];
                    if (games.Contains(first) || ratings.ContainsKey(first)) oldFolders.Add(folder);
                }
            }
        }

        oldFolders.Sort((a, b) => b.Length.CompareTo(a.Length));
        // A rollback that did not finish is finished by rolling back again, never by migrating into it.
        return new LayoutMigrationPlan(roots, moves, merges, stays, filed, authorsMerged, outOfScope, oldFolders,
            [.. FindUnfinished(roots).Where(journal => !WasBeingUndone(journal))]);
    }

    private static bool IsCard(string path) => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);

    private static LayoutDocumentKind? DocumentKindOf(string path)
    {
        var name = Path.GetFileName(path);
        var parent = Path.GetFileName(Path.GetDirectoryName(path));
        var grand = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path)));
        if (string.Equals(parent, FetchedData, PathComparison.Comparison)
            && string.Equals(grand, Metadata, PathComparison.Comparison)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return LayoutDocumentKind.Sidecar;
        if (!string.Equals(parent, Metadata, PathComparison.Comparison)) return null;
        if (string.Equals(name, CharacterAnnotationStore.DocumentFileName, PathComparison.Comparison))
            return LayoutDocumentKind.Characters;
        if (string.Equals(name, LocalSourceStore.DocumentFileName, PathComparison.Comparison))
            return LayoutDocumentKind.LocalSource;
        return null;
    }

    private static IEnumerable<LayoutOutOfScope> CountOutOfScope(string root, List<string> providerDirs, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>(PathComparison.Comparer);
        var ownMetadata = Path.Combine(root, Metadata);
        foreach (var file in EnumerateFiles(root, ct))
        {
            if (file.StartsWith(ownMetadata + Path.DirectorySeparatorChar, PathComparison.Comparison)) continue;
            if (providerDirs.Any(p => file.StartsWith(p + Path.DirectorySeparatorChar, PathComparison.Comparison))) continue;
            var parts = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar);
            // Where: the top folder, or the folder under the import folder.
            var location = parts.Length == 1 ? "" : parts.Length > 2 ? Path.Combine(parts[0], parts[1]) : parts[0];
            counts[location] = counts.GetValueOrDefault(location) + 1;
        }
        return counts.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c => new LayoutOutOfScope(c.Key, c.Value));
    }

    private static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // A link inside the library leads elsewhere; following it would move files that are not the library's.
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private static IEnumerable<string> EnumerateFiles(string directory, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*", Walk))
        {
            ct.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string directory, CancellationToken ct)
    {
        foreach (var folder in Directory.EnumerateDirectories(directory, "*", Walk))
        {
            ct.ThrowIfCancellationRequested();
            yield return folder;
        }
    }

    private static bool SameBytes(string a, string b)
    {
        if (new FileInfo(a).Length != new FileInfo(b).Length) return false;
        using var sa = File.OpenRead(a);
        using var sb = File.OpenRead(b);
        return SHA256.HashData(sa).AsSpan().SequenceEqual(SHA256.HashData(sb));
    }

    // ---------------------------------------------------------------- journal

    private sealed record JournalSource(string Path, string? Backup, bool Removed);

    private sealed record JournalEntry(
        string Op,
        string? Root = null,
        string? From = null,
        string? To = null,
        long? Length = null,
        long? Written = null,
        string? Reason = null,
        string? Kind = null,
        string? Backup = null,
        IReadOnlyList<JournalSource>? Sources = null,
        DateTimeOffset? At = null);

    /// <summary>One root's journal: JSON lines, each flushed to disk before the next step.</summary>
    private sealed class Journal : IDisposable
    {
        private readonly FileStream _stream;
        private int _backups;

        public string Path { get; }
        public string BackupDirectory => System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(Path)!, System.IO.Path.GetFileNameWithoutExtension(Path), "backup");

        public Journal(string path)
        {
            Path = path;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var tornLine = File.Exists(path) && new FileInfo(path).Length > 0 && !EndsWithNewLine(path);
            _stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            // A line cut short by a killed process must not swallow the next one.
            if (tornLine) _stream.Write("\n"u8);
            _backups = Directory.Exists(BackupDirectory) ? Directory.GetFiles(BackupDirectory).Length : 0;
        }

        private static bool EndsWithNewLine(string path)
        {
            using var read = File.OpenRead(path);
            read.Seek(-1, SeekOrigin.End);
            return read.ReadByte() == '\n';
        }

        public void Write(JournalEntry entry)
        {
            var line = JsonSerializer.Serialize(entry, JournalJson) + "\n";
            _stream.Write(Encoding.UTF8.GetBytes(line));
            _stream.Flush(flushToDisk: true);
        }

        public string BackUp(string file)
        {
            Directory.CreateDirectory(BackupDirectory);
            var backup = System.IO.Path.Combine(BackupDirectory, $"{++_backups:D6}{System.IO.Path.GetExtension(file)}");
            File.Copy(file, backup, overwrite: false);
            return backup;
        }

        public void Dispose() => _stream.Dispose();
    }

    private static List<JournalEntry> ReadJournal(string path)
    {
        var entries = new List<JournalEntry>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                if (JsonSerializer.Deserialize<JournalEntry>(line, JournalJson) is { } entry) entries.Add(entry);
            }
            catch (JsonException)
            {
                // A line torn by a killed process: everything before it is intact.
            }
        }
        return entries;
    }

    /// <summary>
    /// Journals of runs that neither finished nor were undone, and of rollbacks
    /// that did not finish. Newest last.
    /// </summary>
    public static IReadOnlyList<string> FindUnfinished(IEnumerable<string> roots)
    {
        var found = new List<string>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(PathComparison.Comparer))
        {
            var folder = Path.Combine(Path.GetFullPath(root), Metadata);
            if (!Directory.Exists(folder)) continue;
            foreach (var journal in Directory.GetFiles(folder, JournalPrefix + "*.json").Order(StringComparer.Ordinal))
            {
                var ops = ReadJournal(journal).Select(e => e.Op).ToList();
                var undoing = ops.LastIndexOf("undoing");
                var finished = undoing >= 0 ? ops.Skip(undoing).Contains("undone") : ops.Contains("done");
                if (!finished) found.Add(journal);
            }
        }
        return found;
    }

    /// <summary>
    /// A journal name for a run starting at <paramref name="at"/>, never one
    /// that exists: two runs in the same second must not share a journal.
    /// Numbered from 02, so names still sort in the order the runs started.
    /// </summary>
    private static string NewJournalPath(string root, DateTimeOffset at)
    {
        var stem = Path.Combine(root, Metadata, $"{JournalPrefix}{at:yyyyMMddHHmmss}");
        var path = stem + ".json";
        for (var n = 2; File.Exists(path); n++) path = $"{stem}_{n:D2}.json";
        return path;
    }

    /// <summary>
    /// The run that can be moved back now, per root: the newest journal that
    /// was not undone, when its run finished or was stopped. An older run is
    /// never offered while a newer one stands (undone runs aside), and a run
    /// that was killed is offered for resuming, not here. Newest last.
    /// </summary>
    public static IReadOnlyList<LayoutMigrationRun> FindUndoable(IEnumerable<string> roots)
    {
        var runs = new List<LayoutMigrationRun>();
        foreach (var root in roots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Path.GetFullPath).Distinct(PathComparison.Comparer))
        {
            var folder = Path.Combine(root, Metadata);
            if (!Directory.Exists(folder)) continue;
            foreach (var journal in Directory.GetFiles(folder, JournalPrefix + "*.json").OrderDescending(StringComparer.Ordinal))
            {
                var entries = ReadJournal(journal);
                var ops = entries.Select(e => e.Op).ToList();
                if (ops.Contains("undone")) continue;
                if (ops.Contains("done") || ops.Contains("stopped"))
                {
                    var returned = new HashSet<string>(entries.Where(e => e.Op == "returned").Select(e => e.To!), PathComparison.Comparer);
                    runs.Add(new LayoutMigrationRun(journal, root,
                        entries.FirstOrDefault(e => e.Op == "header")?.At,
                        entries.Count(e => e.Op == "moved" && !returned.Contains(e.To!))));
                }
                break;
            }
        }
        return [.. runs.OrderBy(r => r.At ?? DateTimeOffset.MinValue)];
    }

    /// <summary>Whether this journal's run was being moved back when it stopped.</summary>
    public static bool WasBeingUndone(string journalPath)
        => ReadJournal(journalPath).Any(e => e.Op == "undoing");

    /// <summary>
    /// A move written ahead but never confirmed (the process died): moved when
    /// the destination has the source's length and write time and the source
    /// is gone, otherwise not.
    /// </summary>
    private static void SettleDangling(Journal journal, List<JournalEntry> entries)
    {
        var open = new Dictionary<string, JournalEntry>(PathComparison.Comparer);
        foreach (var entry in entries)
        {
            if (entry.Op == "begin") open[entry.From!] = entry;
            else if (entry.Op is "moved" or "stayed" && entry.From is { } from) open.Remove(from);
        }
        foreach (var begin in open.Values)
        {
            var settled = !File.Exists(begin.From) && Matches(begin.To!, begin.Length, begin.Written)
                ? begin with { Op = "moved" }
                : begin with { Op = "stayed", Reason = nameof(LayoutStayReason.Failed) };
            journal.Write(settled);
            entries.Add(settled);
        }
    }

    private static bool Matches(string path, long? length, long? written)
    {
        var info = new FileInfo(path);
        return info.Exists && info.Length == length && info.LastWriteTimeUtc.Ticks == written;
    }

    // ---------------------------------------------------------------- applying

    public static async Task<LayoutMigrationResult> ApplyAsync(
        LayoutMigrationPlan plan, LayoutMigrationSteps steps,
        IProgress<LayoutMigrationProgress>? progress, CancellationToken ct)
    {
        var now = steps.Now ?? (() => DateTimeOffset.Now);
        var stayed = new List<LayoutStay>(plan.Stays);
        var journals = new Dictionary<string, Journal>(PathComparison.Comparer);
        var moved = new Dictionary<string, List<(string From, string To)>>(PathComparison.Comparer);
        var repoint = new Dictionary<string, List<(string From, string To)>>(PathComparison.Comparer);
        var stopped = false;
        try
        {
            // 1. A journal per root with work, continuing an unfinished one when there is.
            foreach (var root in plan.Roots)
            {
                bool Under(string path) => path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison.Comparison);
                var hasWork = plan.Moves.Any(m => m.Root == root) || plan.Merges.Any(m => m.Root == root)
                              || plan.OldFolders.Any(Under) || plan.UnfinishedJournals.Any(Under);
                if (!hasWork) continue;
                var unfinished = plan.UnfinishedJournals.LastOrDefault(j =>
                    string.Equals(Path.GetDirectoryName(Path.GetDirectoryName(j)), root, PathComparison.Comparison));
                var path = unfinished ?? NewJournalPath(root, now());
                var earlier = unfinished is null ? null : ReadJournal(unfinished);
                var journal = new Journal(path);
                journals[root] = journal;
                moved[root] = [];
                repoint[root] = [];
                if (earlier is null)
                {
                    journal.Write(new JournalEntry("header", Root: root, At: now()));
                }
                else
                {
                    SettleDangling(journal, earlier);
                    // A killed run never re-pointed its moves: this run does, with its own.
                    // Pairs a stopped run already re-pointed are skipped by the stores.
                    repoint[root].AddRange(earlier.Where(e => e.Op == "moved").Select(e => (e.From!, e.To!)));
                }
            }

            // 2. Every rating the old folders imply, on disk before anything moves.
            var notFiled = new HashSet<string>(PathComparison.Comparer);
            if (plan.Ratings.Count > 0)
            {
                foreach (var path in await steps.RecordRatings(plan.Ratings, ct).ConfigureAwait(false))
                    notFiled.Add(Path.GetFullPath(path));
            }
            foreach (var journal in journals.Values) journal.Write(new JournalEntry("ratings", Length: plan.Ratings.Count));

            // 3. The moves, one by one; cancellation only between files.
            var done = 0;
            foreach (var move in plan.Moves)
            {
                if (ct.IsCancellationRequested) { stopped = true; break; }
                var journal = journals[move.Root];
                LayoutStay? stay = null;
                if (notFiled.Contains(move.Source))
                {
                    stay = new(move.Root, move.Source, move.Destination, LayoutStayReason.RatingNotSaved);
                }
                else if (!Matches(move.Source, move.Length, move.WrittenUtc.Ticks))
                {
                    stay = new(move.Root, move.Source, move.Destination, LayoutStayReason.Changed);
                }
                else
                {
                    foreach (var created in MissingFolders(Path.GetDirectoryName(move.Destination)!))
                        journal.Write(new JournalEntry("mkdir", To: created));
                    var begin = new JournalEntry("begin", From: move.Source, To: move.Destination,
                        Length: move.Length, Written: move.WrittenUtc.Ticks);
                    journal.Write(begin);
                    var result = await steps.Mover.MoveAsync(move.Source, move.Destination, CancellationToken.None)
                        .ConfigureAwait(false);
                    steps.AfterMove?.Invoke(move.Source);
                    if (result.Outcome == LibraryCardMoveOutcome.Moved)
                    {
                        journal.Write(begin with { Op = "moved" });
                        moved[move.Root].Add((move.Source, move.Destination));
                        repoint[move.Root].Add((move.Source, move.Destination));
                    }
                    else
                    {
                        stay = new(move.Root, move.Source, move.Destination, result.Outcome switch
                        {
                            LibraryCardMoveOutcome.Collision => LayoutStayReason.Collision,
                            LibraryCardMoveOutcome.DuplicateAtDestination => LayoutStayReason.Duplicate,
                            LibraryCardMoveOutcome.SourceMissing => LayoutStayReason.Changed,
                            _ => LayoutStayReason.Failed,
                        }, result.Error);
                        journal.Write(begin with { Op = "stayed", Reason = stay.Reason.ToString() });
                    }
                }
                if (stay is not null)
                {
                    stayed.Add(stay);
                    if (stay.Reason is LayoutStayReason.RatingNotSaved or LayoutStayReason.Changed)
                        journal.Write(new JournalEntry("stayed", From: move.Source, To: move.Destination, Reason: stay.Reason.ToString()));
                }
                progress?.Report(new(++done, plan.Moves.Count));
            }

            // 4. Documents: merged after the files, so they only point at what moved.
            foreach (var merge in plan.Merges)
                await MergeAsync(merge, journals[merge.Root], stayed).ConfigureAwait(false);

            // 5. Everything keyed by path follows, once per root.
            foreach (var (_, pairs) in repoint.Where(m => m.Value.Count > 0))
                await steps.PathsMoved(pairs, CancellationToken.None).ConfigureAwait(false);

            // 6. Old folders that are now empty, deepest first.
            foreach (var folder in plan.OldFolders)
            {
                if (!Directory.Exists(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) continue;
                // The deepest root holding it: roots can be nested.
                var root = plan.Roots
                    .Where(r => folder.StartsWith(r + Path.DirectorySeparatorChar, PathComparison.Comparison))
                    .MaxBy(r => r.Length)!;
                if (!journals.ContainsKey(root)) continue;
                try
                {
                    Directory.Delete(folder);
                    journals[root].Write(new JournalEntry("rmdir", From: folder));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A folder something else holds open stays; it is empty and harmless.
                }
            }

            foreach (var journal in journals.Values) journal.Write(new JournalEntry(stopped ? "stopped" : "done", At: now()));
            return new LayoutMigrationResult(moved.Values.Sum(m => m.Count), stayed, stopped,
                [.. journals.Values.Select(j => j.Path)]);
        }
        finally
        {
            foreach (var journal in journals.Values) journal.Dispose();
        }
    }

    /// <summary>The folders a move into <paramref name="directory"/> will create, outermost first.</summary>
    private static List<string> MissingFolders(string directory)
    {
        var missing = new List<string>();
        for (var d = directory; d is not null && !Directory.Exists(d); d = Path.GetDirectoryName(d))
            missing.Insert(0, d);
        return missing;
    }

    /// <summary>
    /// Merges documents into their destination, after the moves. Originals are
    /// backed up first. A source document stays in place while any file it
    /// speaks for is still in its folder; otherwise it is removed.
    /// </summary>
    private static async Task MergeAsync(LayoutDocumentMerge merge, Journal journal, List<LayoutStay> stayed)
    {
        var sources = merge.Sources.Where(File.Exists).ToList();
        if (sources.Count == 0) return;
        var destinationFolder = FolderOfDocument(merge.Kind, merge.Destination);
        try
        {
            var existed = File.Exists(merge.Destination);
            var destinationBackup = existed ? journal.BackUp(merge.Destination) : null;
            var records = sources.Select(s => (Path: s, Backup: journal.BackUp(s))).ToList();

            var merged = Merge(merge.Kind, existed ? merge.Destination : null, sources, destinationFolder);
            foreach (var created in MissingFolders(Path.GetDirectoryName(merge.Destination)!))
                journal.Write(new JournalEntry("mkdir", To: created));
            Directory.CreateDirectory(Path.GetDirectoryName(merge.Destination)!);
            if (existed && merge.Kind == LayoutDocumentKind.LocalSource)
            {
                // The destination folder already says which local source it is.
            }
            else if (merged is null)
            {
                // A single document with nothing to change: its bytes are copied as they are.
                File.Copy(sources[0], merge.Destination, overwrite: false);
            }
            else
            {
                await AtomicJsonFile.WriteAsync(merge.Destination, merged, DocumentJson, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var journalSources = new List<JournalSource>();
            foreach (var (path, backup) in records)
            {
                var removed = !SpeaksForFilesIn(merge.Kind, path, FolderOfDocument(merge.Kind, path));
                if (removed) File.Delete(path);
                journalSources.Add(new(path, backup, removed));
            }
            var written = new FileInfo(merge.Destination);
            journal.Write(new JournalEntry("doc", To: merge.Destination, Kind: merge.Kind.ToString(),
                Backup: destinationBackup, Sources: journalSources,
                Length: written.Length, Written: written.LastWriteTimeUtc.Ticks));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            foreach (var source in sources)
                stayed.Add(new(merge.Root, source, merge.Destination, LayoutStayReason.Failed, ex.Message));
        }
    }

    /// <summary>The folder whose files a document speaks for.</summary>
    private static string FolderOfDocument(LayoutDocumentKind kind, string document)
    {
        var metadata = Path.GetDirectoryName(document)!;
        if (kind == LayoutDocumentKind.Sidecar) metadata = Path.GetDirectoryName(metadata)!;
        return Path.GetDirectoryName(metadata)!;
    }

    /// <summary>The file names a document speaks for.</summary>
    private static IEnumerable<string> NamesIn(LayoutDocumentKind kind, JsonNode? document) => kind switch
    {
        LayoutDocumentKind.Sidecar => (document?["localFileNames"] as JsonArray ?? [])
            .Select(n => n?.GetValue<string>()).OfType<string>(),
        LayoutDocumentKind.Characters => (document?["cards"] as JsonObject)?.Select(p => p.Key) ?? [],
        _ => [],
    };

    /// <summary>Whether a file of that name is in <paramref name="folder"/> or one of its subfolders.</summary>
    private static bool HasFile(string folder, string name)
        => Directory.Exists(folder)
           && (File.Exists(Path.Combine(folder, name))
               || Directory.EnumerateDirectories(folder)
                   .Where(d => !string.Equals(Path.GetFileName(d), Metadata, PathComparison.Comparison))
                   .Any(d => File.Exists(Path.Combine(d, name))));

    private static bool SpeaksForFilesIn(LayoutDocumentKind kind, string document, string folder)
    {
        if (kind == LayoutDocumentKind.LocalSource)
            // The identity of a local folder: it stays while the folder still holds cards.
            return Directory.Exists(folder) && Directory.EnumerateFiles(folder).Any()
                   || Directory.Exists(folder) && Directory.EnumerateDirectories(folder)
                       .Any(d => !string.Equals(Path.GetFileName(d), Metadata, PathComparison.Comparison));
        var node = JsonNode.Parse(File.ReadAllText(document));
        return NamesIn(kind, node).Any(name => HasFile(folder, name));
    }

    /// <summary>
    /// The merged document, or null when the single source can be copied as is.
    /// Sidecars: the most recently fetched wins for every field, the local file
    /// names are the union (a source's name only when that file is now in the
    /// destination folder), and the user's own rating is the newest one set.
    /// Character notes: the destination's entries plus each source's entries for
    /// cards now in the destination folder. A local source keeps the destination's.
    /// </summary>
    private static JsonNode? Merge(LayoutDocumentKind kind, string? existing, List<string> sources, string folder)
    {
        var parsed = sources.Select(s => JsonNode.Parse(File.ReadAllText(s))!).ToList();
        var current = existing is null ? null : JsonNode.Parse(File.ReadAllText(existing));

        switch (kind)
        {
            case LayoutDocumentKind.Sidecar:
            {
                var all = (current is null ? parsed : [current, .. parsed])
                    .OrderByDescending(d => FetchedAt(d)).ToList();
                var names = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in NamesIn(kind, current)) if (seen.Add(name)) names.Add(name);
                foreach (var source in parsed)
                    foreach (var name in NamesIn(kind, source))
                        if (HasFile(folder, name) && seen.Add(name)) names.Add(name);

                if (current is null && parsed.Count == 1
                    && NamesIn(kind, parsed[0]).Count() == names.Count) return null;

                var result = all[0].DeepClone();
                result["localFileNames"] = new JsonArray([.. names.Select(n => (JsonNode)JsonValue.Create(n)!)]);
                var userRating = all.Select(d => d["userRating"]).FirstOrDefault(r => r is not null);
                if (userRating is not null) result["userRating"] = userRating.DeepClone();
                return result;
            }
            case LayoutDocumentKind.Characters:
            {
                var result = (current ?? parsed[0]).DeepClone();
                var cards = result["cards"] as JsonObject ?? new JsonObject();
                result["cards"] = cards;
                foreach (var source in parsed)
                    foreach (var (name, entry) in (source["cards"] as JsonObject) ?? [])
                        if (!cards.ContainsKey(name) && HasFile(folder, name))
                            cards[name] = entry?.DeepClone();
                if (current is null && parsed.Count == 1
                    && cards.Count == ((parsed[0]["cards"] as JsonObject)?.Count ?? 0)) return null;
                return result;
            }
            default:
                return current is null && parsed.Count >= 1 ? null : current!.DeepClone();
        }
    }

    private static DateTimeOffset FetchedAt(JsonNode document)
        => document["fetchedAt"] is JsonValue value && value.TryGetValue<string>(out var text)
           && DateTimeOffset.TryParse(text, out var at) ? at : DateTimeOffset.MinValue;

    // ---------------------------------------------------------------- rolling back

    /// <summary>
    /// Moves everything one journal moved back, last first, with the same
    /// guards. A file changed, moved or deleted since is skipped and reported.
    /// Filed ratings stay: they agree with the folders the cards are back in.
    /// </summary>
    public static async Task<LayoutMigrationResult> RollbackAsync(
        string journalPath, LayoutMigrationSteps steps,
        IProgress<LayoutMigrationProgress>? progress, CancellationToken ct)
    {
        var entries = ReadJournal(journalPath);
        var root = entries.FirstOrDefault(e => e.Op == "header")?.Root ?? Path.GetDirectoryName(Path.GetDirectoryName(journalPath))!;
        var stayed = new List<LayoutStay>();
        var back = new List<(string From, string To)>();
        var stopped = false;
        using (var journal = new Journal(journalPath))
        {
            SettleDangling(journal, entries);
            journal.Write(new JournalEntry("undoing"));

            // Folders the run removed, so files and documents have somewhere to go.
            foreach (var folder in entries.Where(e => e.Op == "rmdir").Select(e => e.From!).OrderBy(f => f.Length))
                Directory.CreateDirectory(folder);

            // Documents first, newest first, so a source path is free again.
            // Each is marked restored, so a rollback run again skips it.
            var restored = new HashSet<string>(
                entries.Where(e => e.Op == "doc-restored").Select(e => e.To!), PathComparison.Comparer);
            foreach (var doc in entries.Where(e => e.Op == "doc" && !restored.Contains(e.To!)).Reverse())
            {
                if (RestoreDocument(doc, root, stayed))
                    journal.Write(new JournalEntry("doc-restored", To: doc.To));
            }

            var alreadyBack = new HashSet<string>(
                entries.Where(e => e.Op == "returned").Select(e => e.To!), PathComparison.Comparer);
            var toMove = entries.Where(e => e.Op == "moved" && !alreadyBack.Contains(e.To!)).Reverse().ToList();
            var done = 0;
            foreach (var entry in toMove)
            {
                if (ct.IsCancellationRequested) { stopped = true; break; }
                if (!File.Exists(entry.To) && Matches(entry.From!, entry.Length, entry.Written))
                {
                    // Moved back by a rollback that was killed before it said so.
                    journal.Write(entry with { Op = "returned" });
                    back.Add((entry.To!, entry.From!));
                }
                else if (!Matches(entry.To!, entry.Length, entry.Written))
                {
                    stayed.Add(new(root, entry.To!, entry.From, LayoutStayReason.Changed));
                }
                else
                {
                    var result = await steps.Mover.MoveAsync(entry.To!, entry.From!, CancellationToken.None).ConfigureAwait(false);
                    steps.AfterMove?.Invoke(entry.To!);
                    if (result.Outcome == LibraryCardMoveOutcome.Moved)
                    {
                        journal.Write(entry with { Op = "returned" });
                        back.Add((entry.To!, entry.From!));
                    }
                    else
                    {
                        stayed.Add(new(root, entry.To!, entry.From, result.Outcome switch
                        {
                            LibraryCardMoveOutcome.Collision => LayoutStayReason.Collision,
                            LibraryCardMoveOutcome.DuplicateAtDestination => LayoutStayReason.Duplicate,
                            LibraryCardMoveOutcome.SourceMissing => LayoutStayReason.Changed,
                            _ => LayoutStayReason.Failed,
                        }, result.Error));
                    }
                }
                progress?.Report(new(++done, toMove.Count));
            }

            // Everything this journal has moved back, including what a killed
            // rollback moved but never re-pointed; already re-pointed pairs are skipped by the stores.
            List<(string From, string To)> repoint =
                [.. entries.Where(e => e.Op == "returned").Select(e => (e.To!, e.From!)), .. back];
            if (repoint.Count > 0)
                await steps.PathsMoved(repoint, CancellationToken.None).ConfigureAwait(false);

            // Folders the run created, now empty, deepest first.
            foreach (var folder in entries.Where(e => e.Op == "mkdir").Select(e => e.To!)
                         .Distinct(PathComparison.Comparer).OrderByDescending(f => f.Length))
            {
                try
                {
                    if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                        Directory.Delete(folder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Empty and harmless where it is.
                }
            }

            journal.Write(new JournalEntry(stopped ? "undo-stopped" : "undone"));
        }
        return new LayoutMigrationResult(back.Count, stayed, stopped, [journalPath]);
    }

    /// <returns>False when restoring failed outright.</returns>
    private static bool RestoreDocument(JournalEntry doc, string root, List<LayoutStay> stayed)
    {
        var destination = doc.To!;
        try
        {
            // Only our own merge is replaced: a document changed since stays as it is.
            var ours = Matches(destination, doc.Length, doc.Written);
            if (ours)
            {
                if (doc.Backup is { } backup) File.Copy(backup, destination, overwrite: true);
                else File.Delete(destination);
            }
            else if (File.Exists(destination))
            {
                stayed.Add(new(root, destination, null, LayoutStayReason.Changed));
            }

            foreach (var source in doc.Sources ?? [])
            {
                if (!source.Removed || source.Backup is null) continue;
                if (File.Exists(source.Path))
                {
                    stayed.Add(new(root, source.Path, null, LayoutStayReason.Collision));
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(source.Path)!);
                File.Copy(source.Backup, source.Path, overwrite: false);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stayed.Add(new(root, destination, null, LayoutStayReason.Failed, ex.Message));
            return false;
        }
    }
}

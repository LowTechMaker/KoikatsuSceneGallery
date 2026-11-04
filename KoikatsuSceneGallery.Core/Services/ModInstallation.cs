using System.Collections.Concurrent;
using System.Text.Json;

namespace KoikatsuSceneGallery.Services;

public sealed record InstalledMod(string Path, ZipmodManifest Manifest);

/// <summary>
/// Remembers manifests by path, size and write time, so looking at a mods folder of
/// thousands of files a second time opens only the files that changed.
/// </summary>
public sealed class ZipmodManifestCache
{
    private readonly ConcurrentDictionary<string, (long Length, DateTime WriteTime, ZipmodManifest? Manifest)> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public ZipmodManifest? Read(string path)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }

        if (_entries.TryGetValue(path, out var cached) && cached.Length == info.Length && cached.WriteTime == info.LastWriteTimeUtc)
            return cached.Manifest;
        var manifest = ZipmodManifestReader.TryRead(path);
        _entries[path] = (info.Length, info.LastWriteTimeUtc, manifest);
        return manifest;
    }
}

/// <summary>The mods already in one game's mods folder, by GUID.</summary>
public sealed class InstalledModIndex
{
    private readonly Dictionary<string, List<InstalledMod>> _byGuid;

    private InstalledModIndex(Dictionary<string, List<InstalledMod>> byGuid) => _byGuid = byGuid;

    public static InstalledModIndex Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

    public int Count => _byGuid.Values.Sum(list => list.Count);

    public IReadOnlyList<InstalledMod> Find(string guid)
        => _byGuid.TryGetValue(guid, out var mods) ? mods : [];

    /// <summary>Every .zip and .zipmod below <paramref name="modsDirectory"/>; a missing folder is an empty index.</summary>
    public static InstalledModIndex Build(string modsDirectory, Func<string, ZipmodManifest?> readManifest, CancellationToken ct)
    {
        if (!Directory.Exists(modsDirectory)) return Empty;
        var files = Directory.EnumerateFiles(modsDirectory, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
            })
            .Where(ZipmodManifestReader.HasModExtension)
            .ToList();

        var found = new ConcurrentBag<InstalledMod>();
        Parallel.ForEach(files, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, path =>
        {
            if (readManifest(path) is { } manifest) found.Add(new InstalledMod(path, manifest));
        });

        var byGuid = new Dictionary<string, List<InstalledMod>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in found.OrderBy(mod => mod.Path, StringComparer.OrdinalIgnoreCase))
        {
            if (!byGuid.TryGetValue(mod.Manifest.Guid, out var list)) byGuid[mod.Manifest.Guid] = list = [];
            list.Add(mod);
        }
        return new InstalledModIndex(byGuid);
    }
}

public sealed record ModInstallCandidate(string SourcePath, ZipmodManifest? Manifest)
{
    public string FileName => Path.GetFileName(SourcePath);
}

/// <param name="ModsDirectory">The game's <c>mods</c> folder, searched for copies already installed.</param>
/// <param name="InstallDirectory">The gallery's own folder inside it, where new mods are written.</param>
public sealed record ModInstallTarget(ModGame Game, string ModsDirectory, string InstallDirectory);

public enum ModInstallAction
{
    /// <summary>No copy of this mod in the game yet.</summary>
    Install,
    /// <summary>Only older copies are installed: the new one is written beside them.</summary>
    Update,
    AlreadyInstalled,
    NewerInstalled,
    NotForThisGame,
    /// <summary>The same mod, in a newer or equal version, is in the same batch.</summary>
    SupersededInBatch,
    /// <summary>No readable manifest with a GUID: not something Sideloader would load.</summary>
    NotAMod,
}

/// <param name="Game">Null for steps that are about the file, not about a game.</param>
/// <param name="OlderCopies">Installed copies older than this one.</param>
/// <param name="ToBackUp">
/// The older copies the gallery itself installed. Only those are moved out of the mods
/// folder; an older copy anywhere else — the user's own folders, a modpack — is left
/// where it is, and Sideloader loads the newest of the copies.
/// </param>
public sealed record ModInstallStep(
    ModInstallCandidate Candidate,
    ModGame? Game,
    ModInstallAction Action,
    string? DestinationPath,
    IReadOnlyList<InstalledMod> OlderCopies,
    IReadOnlyList<InstalledMod> ToBackUp,
    InstalledMod? ExistingCopy = null)
{
    public bool WillWrite => Action is ModInstallAction.Install or ModInstallAction.Update;
}

public static class ModInstallPlanner
{
    public static IReadOnlyList<ModInstallStep> Plan(
        IReadOnlyList<ModInstallCandidate> candidates,
        IReadOnlyList<ModInstallTarget> targets,
        Func<ModGame, InstalledModIndex> installed)
    {
        var steps = new List<ModInstallStep>();

        // The newest of each GUID in the batch is the one installed; on a tie, the first.
        var keep = new Dictionary<string, ModInstallCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (candidate.Manifest is not { } manifest) continue;
            if (!keep.TryGetValue(manifest.Guid, out var best)
                || ModVersion.Compare(manifest.Version, best.Manifest!.Version) > 0)
                keep[manifest.Guid] = candidate;
        }

        // Names given out in this plan, so two different mods called "mod.zipmod" do not collide.
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (candidate.Manifest is not { } manifest)
            {
                steps.Add(new ModInstallStep(candidate, null, ModInstallAction.NotAMod, null, [], []));
                continue;
            }
            if (!ReferenceEquals(keep[manifest.Guid], candidate))
            {
                steps.Add(new ModInstallStep(candidate, null, ModInstallAction.SupersededInBatch, null, [], []));
                continue;
            }

            var supported = manifest.SupportedGames();
            foreach (var target in targets)
            {
                if (!supported.Contains(target.Game))
                {
                    steps.Add(new ModInstallStep(candidate, target.Game, ModInstallAction.NotForThisGame, null, [], []));
                    continue;
                }

                var copies = installed(target.Game).Find(manifest.Guid);
                var newest = copies.OrderByDescending(copy => copy.Manifest.Version, Comparer<string?>.Create(ModVersion.Compare)).FirstOrDefault();
                if (newest is not null)
                {
                    var comparison = ModVersion.Compare(newest.Manifest.Version, manifest.Version);
                    if (comparison >= 0)
                    {
                        steps.Add(new ModInstallStep(candidate, target.Game,
                            comparison == 0 ? ModInstallAction.AlreadyInstalled : ModInstallAction.NewerInstalled,
                            null, [], [], newest));
                        continue;
                    }
                }

                var toBackUp = copies.Where(copy => IsInside(copy.Path, target.InstallDirectory)).ToList();
                var destination = UniqueDestination(target.InstallDirectory, candidate.FileName, toBackUp, reserved);
                steps.Add(new ModInstallStep(candidate, target.Game,
                    copies.Count == 0 ? ModInstallAction.Install : ModInstallAction.Update,
                    destination, copies, toBackUp, newest));
            }
        }
        return steps;
    }

    internal static bool IsInside(string path, string directory)
    {
        var full = Path.GetFullPath(path);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string UniqueDestination(
        string directory, string fileName, IReadOnlyList<InstalledMod> leaving, HashSet<string> reserved)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var n = 1; ; n++)
        {
            var name = n == 1 ? fileName : $"{stem} ({n}){extension}";
            var path = Path.Combine(directory, name);
            var freed = leaving.Any(copy => string.Equals(Path.GetFullPath(copy.Path), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
            if ((freed || !File.Exists(path)) && reserved.Add(path)) return path;
        }
    }
}

public sealed record ModInstallOutcome(ModInstallStep Step, bool Succeeded, string? Error, IReadOnlyList<string> BackedUpTo);

public static class ModInstaller
{
    /// <summary>
    /// Copies each mod in; the source file stays where it is. A file is written under a
    /// temporary name Sideloader ignores and renamed into place last, so a failure never
    /// leaves half a mod in the game. Older copies the gallery installed are moved to
    /// <paramref name="backupDirectory"/>, outside the mods folder, and moved back if the
    /// install fails. Every attempt is appended to <paramref name="logPath"/> as one JSON line.
    /// </summary>
    public static IReadOnlyList<ModInstallOutcome> Execute(
        IEnumerable<ModInstallStep> steps,
        string backupDirectory,
        string? logPath,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var outcomes = new List<ModInstallOutcome>();
        foreach (var step in steps.Where(step => step.WillWrite))
        {
            ct.ThrowIfCancellationRequested();
            outcomes.Add(Install(step, backupDirectory));
        }
        if (logPath is not null && outcomes.Count > 0) AppendLog(logPath, outcomes, now);
        return outcomes;
    }

    private static ModInstallOutcome Install(ModInstallStep step, string backupDirectory)
    {
        var destination = step.DestinationPath!;
        var temporary = destination + ".sgtmp";
        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(step.Candidate.SourcePath, temporary, overwrite: true);

            foreach (var copy in step.ToBackUp)
            {
                var folder = Path.Combine(backupDirectory, step.Game?.ToString() ?? "Unknown");
                Directory.CreateDirectory(folder);
                var backup = Unique(Path.Combine(folder, Path.GetFileName(copy.Path)));
                File.Move(copy.Path, backup);
                moved.Add((copy.Path, backup));
            }

            File.Move(temporary, destination, overwrite: false);
            return new ModInstallOutcome(step, true, null, moved.Select(m => m.To).ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            foreach (var (from, to) in moved)
            {
                try { File.Move(to, from); } catch (Exception restore) when (restore is IOException or UnauthorizedAccessException) { }
            }
            return new ModInstallOutcome(step, false, ex.Message, []);
        }
    }

    private static string Unique(string path)
    {
        if (!File.Exists(path)) return path;
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void AppendLog(string logPath, IReadOnlyList<ModInstallOutcome> outcomes, DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            var lines = outcomes.Select(outcome => JsonSerializer.Serialize(new
            {
                time = now,
                game = outcome.Step.Game?.ToString(),
                action = outcome.Step.Action.ToString(),
                guid = outcome.Step.Candidate.Manifest?.Guid,
                version = outcome.Step.Candidate.Manifest?.Version,
                source = outcome.Step.Candidate.SourcePath,
                destination = outcome.Step.DestinationPath,
                backedUp = outcome.Step.ToBackUp.Select(copy => copy.Path).Zip(outcome.BackedUpTo, (from, to) => new { from, to }),
                succeeded = outcome.Succeeded,
                error = outcome.Error,
            }));
            File.AppendAllLines(logPath, lines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}

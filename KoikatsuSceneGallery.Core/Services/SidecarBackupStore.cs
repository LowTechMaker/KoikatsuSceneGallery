using System.Globalization;
using System.Text.Json;
using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>One saved state of a set of sidecars, as it was before a bulk re-fetch.</summary>
/// <param name="Id">Folder name of the backup; sortable, newest last.</param>
/// <param name="Saved">Sidecars that existed and were copied.</param>
/// <param name="Absent">Sidecars that did not exist yet; restoring removes them.</param>
internal sealed record SidecarBackup(
    string ProviderId,
    string Id,
    DateTimeOffset CreatedAt,
    int Saved,
    int Absent);

/// <summary>What a restore did.</summary>
internal readonly record struct SidecarRestoreResult(int Restored, int Removed, int Failed);

/// <summary>
/// Copies of the sidecars a bulk re-fetch is about to overwrite, kept so the
/// whole run can be undone.
/// </summary>
/// <remarks>
/// Two slots per platform, like an A/B partition: starting a run from the top
/// adds a backup and drops the oldest beyond two. Keeping only one would let a
/// second run over bad data replace the last good copy with the bad one.
///
/// A backup records every path the run may write, not only the ones that
/// exist: a sidecar the run creates has no earlier content, and undoing the run
/// means deleting it. The manifest is written last, so a backup cut short —
/// the app closed mid-copy — has none and is neither listed nor restored; it is
/// cleared away on the next backup.
///
/// Restoring takes the same per-path lock the sidecar store writes under, so it
/// cannot interleave with an import writing the same file.
/// </remarks>
internal sealed class SidecarBackupStore(string rootDirectory)
{
    public const int Slots = 2;

    private const string ManifestName = "manifest.json";
    private const string FilesDirectoryName = "files";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Saves the current state of <paramref name="sidecarPaths"/> as a new
    /// backup and prunes the platform's backups to <see cref="Slots"/>.
    /// </summary>
    public async Task<SidecarBackup> CreateAsync(
        string providerId,
        IEnumerable<string> sidecarPaths,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var providerDirectory = ProviderDirectory(providerId);
        Directory.CreateDirectory(providerDirectory);
        RemoveIncomplete(providerDirectory);

        var id = UniqueId(providerDirectory, now);
        var directory = Path.Combine(providerDirectory, id);
        var files = Path.Combine(directory, FilesDirectoryName);
        Directory.CreateDirectory(files);

        var entries = new List<ManifestEntry>();
        foreach (var path in Distinct(sidecarPaths))
            entries.Add(Capture(path, files, entries.Count, ct));

        var manifest = new Manifest(providerId, now, entries);
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, ManifestName), manifest, JsonOptions, ct)
            .ConfigureAwait(false);

        Prune(providerDirectory);
        return Describe(id, manifest);
    }

    /// <summary>
    /// Adds to <paramref name="backup"/> the paths it does not cover yet, in
    /// their current state.
    /// </summary>
    /// <remarks>
    /// For a resumed run: artworks imported since the run began were never
    /// touched by it, so what they hold now is still "before the run".
    /// </remarks>
    public async Task<SidecarBackup> ExtendAsync(
        SidecarBackup backup,
        IEnumerable<string> sidecarPaths,
        CancellationToken ct)
    {
        var directory = Path.Combine(ProviderDirectory(backup.ProviderId), backup.Id);
        var manifest = ReadManifest(directory)
            ?? throw new InvalidOperationException($"Backup {backup.Id} has no manifest.");
        var known = new HashSet<string>(manifest.Entries.Select(e => e.Path), PathComparison.Comparer);
        var files = Path.Combine(directory, FilesDirectoryName);

        var entries = manifest.Entries.ToList();
        foreach (var path in Distinct(sidecarPaths))
        {
            if (known.Contains(path)) continue;
            entries.Add(Capture(path, files, entries.Count, ct));
        }

        if (entries.Count == manifest.Entries.Count) return backup;

        var extended = manifest with { Entries = entries };
        await AtomicJsonFile.WriteAsync(Path.Combine(directory, ManifestName), extended, JsonOptions, ct)
            .ConfigureAwait(false);
        return Describe(backup.Id, extended);
    }

    /// <summary>The platform's complete backups, newest first.</summary>
    public IReadOnlyList<SidecarBackup> List(string providerId)
    {
        var providerDirectory = ProviderDirectory(providerId);
        if (!Directory.Exists(providerDirectory)) return [];

        return [.. Directory.EnumerateDirectories(providerDirectory)
            .Select(directory => (Id: Path.GetFileName(directory), Manifest: ReadManifest(directory)))
            .Where(backup => backup.Manifest is not null)
            .Select(backup => Describe(backup.Id, backup.Manifest!))
            .OrderByDescending(backup => backup.Id, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Puts every sidecar the backup covers back as it was: saved ones are
    /// copied back, ones that did not exist are deleted.
    /// </summary>
    /// <remarks>
    /// Carries on past a file it cannot write, so one locked file does not
    /// leave the rest half restored; the count of those comes back as failed.
    /// </remarks>
    public async Task<SidecarRestoreResult> RestoreAsync(SidecarBackup backup, CancellationToken ct)
    {
        var directory = Path.Combine(ProviderDirectory(backup.ProviderId), backup.Id);
        var manifest = ReadManifest(directory)
            ?? throw new InvalidOperationException($"Backup {backup.Id} has no manifest.");
        var files = Path.Combine(directory, FilesDirectoryName);

        int restored = 0, removed = 0, failed = 0;
        foreach (var entry in manifest.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var writeLock = PathWriteLock.For(entry.Path);
            await writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (entry.BackupFile is { } saved)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Path)!);
                    var temporary = $"{entry.Path}.{Guid.NewGuid():N}.tmp";
                    File.Copy(Path.Combine(files, saved), temporary);
                    File.Move(temporary, entry.Path, overwrite: true);
                    restored++;
                }
                else if (File.Exists(entry.Path))
                {
                    File.Delete(entry.Path);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
            finally
            {
                writeLock.Release();
            }
        }

        return new(restored, removed, failed);
    }

    private string ProviderDirectory(string providerId)
        => Path.Combine(rootDirectory, Uri.EscapeDataString(providerId));

    private static IEnumerable<string> Distinct(IEnumerable<string> paths)
        => paths.Select(Path.GetFullPath).Distinct(PathComparison.Comparer);

    private static ManifestEntry Capture(string path, string filesDirectory, int index, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Under the store's lock, so the copy is never taken of a file half way
        // through being rewritten by an import.
        var writeLock = PathWriteLock.For(path);
        writeLock.Wait(ct);
        try
        {
            if (!File.Exists(path)) return new ManifestEntry(path, null);
            var name = index.ToString("D6", CultureInfo.InvariantCulture) + ".json";
            File.Copy(path, Path.Combine(filesDirectory, name));
            return new ManifestEntry(path, name);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private static string UniqueId(string providerDirectory, DateTimeOffset now)
    {
        var baseId = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var id = baseId;
        for (var n = 1; Directory.Exists(Path.Combine(providerDirectory, id)); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    private static void RemoveIncomplete(string providerDirectory)
    {
        foreach (var directory in Directory.EnumerateDirectories(providerDirectory))
        {
            if (!File.Exists(Path.Combine(directory, ManifestName)))
                TryDelete(directory);
        }
    }

    private static void Prune(string providerDirectory)
    {
        var complete = Directory.EnumerateDirectories(providerDirectory)
            .Where(directory => File.Exists(Path.Combine(directory, ManifestName)))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        foreach (var stale in complete.Skip(Slots))
            TryDelete(stale);
    }

    private static void TryDelete(string directory)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static Manifest? ReadManifest(string directory)
    {
        var path = Path.Combine(directory, ManifestName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SidecarBackup Describe(string id, Manifest manifest)
        => new(
            manifest.ProviderId,
            id,
            manifest.CreatedAt,
            manifest.Entries.Count(e => e.BackupFile is not null),
            manifest.Entries.Count(e => e.BackupFile is null));

    private sealed record Manifest(string ProviderId, DateTimeOffset CreatedAt, IReadOnlyList<ManifestEntry> Entries);

    private sealed record ManifestEntry(string Path, string? BackupFile);
}

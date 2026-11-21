using KoikatsuSceneGallery.Helpers;

namespace KoikatsuSceneGallery.Services;

/// <summary>
/// Installs Sideloader mods that come in with an import into the gallery's own folder
/// under each configured game's <c>mods</c> folder. Planning only reads the disk;
/// nothing is written until <see cref="InstallAsync"/> is called with a confirmed plan.
/// </summary>
public sealed class ModInstallService
{
    private readonly Func<Task<SettingsService.ConfigData>> _loadConfig;
    private readonly IAppLogger _logger;
    private readonly ZipmodManifestCache _manifests = new();

    public ModInstallService(Func<Task<SettingsService.ConfigData>> loadConfig, IAppLogger logger)
    {
        _loadConfig = loadConfig;
        _logger = logger;
    }

    public static string LogPath => Path.Combine(AppPaths.LocalFolder, AppFiles.ModInstallLog);

    private static string BackupRoot => Path.Combine(AppPaths.LocalFolder, AppFiles.ModBackupsFolder);

    public async Task<IReadOnlyList<ModInstallTarget>> GetTargetsAsync()
    {
        var config = await _loadConfig().ConfigureAwait(false);
        var folderName = PathSanitizer.SanitizeRelativePath(config.ModInstallFolderName?.Trim() ?? "");
        if (folderName.Length == 0) folderName = "SceneGallery";

        var targets = new List<ModInstallTarget>();
        void Add(ModGame game, string? gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder)) return;
            var mods = Path.Combine(gameFolder, "mods");
            targets.Add(new ModInstallTarget(game, mods, Path.Combine(mods, folderName)));
        }
        Add(ModGame.Koikatsu, config.KoikatsuGameFolder);
        Add(ModGame.KoikatsuSunshine, config.KoikatsuSunshineGameFolder);
        return targets;
    }

    /// <summary>Reads the manifests of <paramref name="paths"/>; files that are not mods come back without one.</summary>
    public Task<IReadOnlyList<ModInstallCandidate>> ReadCandidatesAsync(IReadOnlyList<string> paths, CancellationToken ct)
        => Task.Run<IReadOnlyList<ModInstallCandidate>>(
            () => paths.Select(path => new ModInstallCandidate(path, _manifests.Read(path))).ToList(), ct);

    public async Task<IReadOnlyList<ModInstallStep>> PlanAsync(
        IReadOnlyList<ModInstallCandidate> candidates, IReadOnlyList<ModInstallTarget> targets, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var indexes = new Dictionary<ModGame, InstalledModIndex>();
            foreach (var target in targets)
            {
                try { indexes[target.Game] = InstalledModIndex.Build(target.ModsDirectory, _manifests.Read, ct); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError("Mods.Scan", ex, target.ModsDirectory);
                    indexes[target.Game] = InstalledModIndex.Empty;
                }
            }
            return ModInstallPlanner.Plan(candidates, targets, game => indexes[game]);
        }, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ModInstallOutcome>> InstallAsync(IReadOnlyList<ModInstallStep> steps, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        var backup = Path.Combine(BackupRoot, now.ToString(AppFiles.BackupTimestampFormat));
        return Task.Run(() =>
        {
            var outcomes = ModInstaller.Execute(steps, backup, LogPath, now, ct);
            foreach (var failed in outcomes.Where(outcome => !outcome.Succeeded))
                _logger.LogError("Mods.Install", new IOException(failed.Error), failed.Step.Candidate.SourcePath);
            return outcomes;
        }, ct);
    }

    /// <summary>
    /// Whether a mod file of this name is in any configured game's mods folder, for the post
    /// checks: a zipmod the post links is not missing when it is already installed. Names only,
    /// so no archive is opened.
    /// </summary>
    public async Task<Func<string, bool>> GetInstalledNameLookupAsync(CancellationToken ct)
    {
        var targets = await GetTargetsAsync().ConfigureAwait(false);
        var names = await Task.Run(() =>
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets.Where(target => Directory.Exists(target.ModsDirectory)))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(target.ModsDirectory, "*", new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 IgnoreInaccessible = true,
                             }))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (ZipmodManifestReader.HasModExtension(file))
                            set.Add(LibraryNameIndex.KeyOf(Path.GetFileName(file)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogError("Mods.ScanNames", ex, target.ModsDirectory);
                }
            }
            return set;
        }, ct).ConfigureAwait(false);
        return name => ZipmodManifestReader.HasModExtension(name) && names.Contains(LibraryNameIndex.KeyOf(name));
    }
}

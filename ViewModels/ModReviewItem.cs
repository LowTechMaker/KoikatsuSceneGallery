using CommunityToolkit.Mvvm.ComponentModel;
using KoikatsuSceneGallery.Services;

namespace KoikatsuSceneGallery.ViewModels;

/// <summary>
/// One mod in the import list. Its check box means "install this", not "select for
/// editing" as a card's does, so it sits on the right of the row, labelled.
/// </summary>
public sealed partial class ModReviewItem : ObservableObject
{
    public ModReviewItem(ModInstallCandidate candidate, string? archiveName)
    {
        Candidate = candidate;
        ArchiveName = archiveName;
    }

    public ModInstallCandidate Candidate { get; }
    public string? ArchiveName { get; }
    public string SourcePath => Candidate.SourcePath;

    public string Title => Candidate.Manifest?.Name ?? Candidate.FileName;
    public string VersionText => Candidate.Manifest?.Version is { Length: > 0 } version ? "v" + version : string.Empty;
    public string Detail => string.Join("  ·  ", new[]
    {
        Candidate.FileName,
        Candidate.Manifest?.Guid,
        ArchiveName,
    }.Where(part => !string.IsNullOrEmpty(part)));

    public IReadOnlyList<ModInstallStep> Steps { get; private set; } = [];

    [ObservableProperty] public partial bool IsChecking { get; set; } = true;
    [ObservableProperty] public partial string StatusText { get; set; } = string.Empty;
    [ObservableProperty] public partial string StatusDetail { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanInstall { get; set; }
    [ObservableProperty] public partial bool Install { get; set; }

    /// <summary>Takes the plan for this mod; it is checked for install when there is anything to write.</summary>
    public void ApplyPlan(IReadOnlyList<ModInstallStep> steps, string statusText, string statusDetail)
    {
        Steps = steps;
        StatusText = statusText;
        StatusDetail = statusDetail;
        CanInstall = steps.Any(step => step.WillWrite);
        Install = CanInstall;
        IsChecking = false;
    }

    public IEnumerable<ModInstallStep> StepsToRun => Install ? Steps.Where(step => step.WillWrite) : [];
}

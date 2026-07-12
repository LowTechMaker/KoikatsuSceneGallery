[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$OutputDirectory,
    [Alias("allow-missing")]
    [switch]$AllowMissing,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

$appRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $appRepo ".."))
$appProject = Join-Path $appRepo "KoikatsuSceneGallery.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $appRepo "artifacts\release\$RuntimeIdentifier"
}

$publishDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$workspacePrefix = $workspaceRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $publishDirectory.StartsWith($workspacePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDirectory must be inside the SceneGallery workspace: $workspaceRoot"
}

$plugins = @(
    [pscustomobject]@{
        Id = "PixivAuthors"
        RepoDirectory = "PixivAuthorsPlugin"
        ProjectFile = "SceneGallery.Plugin.PixivAuthors.csproj"
    },
    [pscustomobject]@{
        Id = "BepisDb"
        RepoDirectory = "BepisDbPlugin"
        ProjectFile = "SceneGallery.Plugin.BepisDb.csproj"
    },
    [pscustomobject]@{
        Id = "Fanbox"
        RepoDirectory = "FanboxWebView2Plugin"
        ProjectFile = "SceneGallery.Plugin.FanboxWebView2.csproj"
    },
    [pscustomobject]@{
        Id = "GitHubReleaseUpdates"
        RepoDirectory = "GitHubReleaseUpdatePlugin"
        ProjectFile = "SceneGallery.Plugin.GitHubReleaseUpdates.csproj"
    }
)

function Get-RepoState {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$RelativePath
    )

    $commitOutput = & git -c "safe.directory=$Path" -C $Path rev-parse HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read git commit for $RelativePath"
    }
    $commit = ($commitOutput -join "").Trim()

    $branchOutput = & git -c "safe.directory=$Path" -C $Path branch --show-current
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read git branch for $RelativePath"
    }
    $branch = ($branchOutput -join "").Trim()
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = "(detached)"
    }

    $status = @(& git -c "safe.directory=$Path" -C $Path status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to read git status for $RelativePath"
    }

    $dirty = $status.Count -gt 0
    if ($dirty) {
        Write-Warning "$RelativePath has uncommitted changes; the release will record dirty=true."
    }

    [pscustomobject]@{
        repoPath = $RelativePath.Replace('\', '/')
        commit = $commit
        branch = $branch
        dirty = $dirty
    }
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$Description
    )

    Write-Host "==> $Description"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

$missingPlugins = @($plugins | Where-Object {
    -not (Test-Path (Join-Path $workspaceRoot $_.RepoDirectory) -PathType Container) -or
    -not (Test-Path (Join-Path (Join-Path $workspaceRoot $_.RepoDirectory) $_.ProjectFile) -PathType Leaf)
})

if ($missingPlugins.Count -gt 0) {
    $missingNames = ($missingPlugins | ForEach-Object { $_.RepoDirectory }) -join ", "
    Write-Warning "Missing plugin sibling repos: $missingNames"

    if (-not $AllowMissing) {
        if ($env:CI -eq "true") {
            throw "Plugin sibling repos are missing. Supply -AllowMissing to skip them in CI."
        }

        $answer = Read-Host "Continue without the missing plugins? [y/N]"
        if ($answer -notmatch '^(?i:y|yes)$') {
            throw "Publishing cancelled because plugin sibling repos are missing."
        }
    }
}

$appState = Get-RepoState -Path $appRepo -RelativePath ([IO.Path]::GetRelativePath($workspaceRoot, $appRepo))
$pluginStates = foreach ($plugin in $plugins) {
    $repoPath = Join-Path $workspaceRoot $plugin.RepoDirectory
    $relativePath = [IO.Path]::GetRelativePath($workspaceRoot, $repoPath)
    $present = $missingPlugins.Id -notcontains $plugin.Id

    if ($present) {
        $state = Get-RepoState -Path $repoPath -RelativePath $relativePath
        [pscustomobject]@{
            id = $plugin.Id
            repoPath = $state.repoPath
            commit = $state.commit
            branch = $state.branch
            dirty = $state.dirty
            present = $true
            included = $true
            outputDirectory = "Plugins/$($plugin.Id)"
        }
    }
    else {
        [pscustomobject]@{
            id = $plugin.Id
            repoPath = $relativePath.Replace('\', '/')
            commit = $null
            branch = $null
            dirty = $null
            present = $false
            included = $false
            outputDirectory = $null
        }
    }
}

if (Test-Path $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $publishDirectory | Out-Null

$platform = $RuntimeIdentifier -replace '^win-', ''
$restoreArguments = if ($NoRestore) { @("--no-restore") } else { @() }
$appPublishArguments = @(
    "publish", $appProject
)
$appPublishArguments += $restoreArguments
$appPublishArguments += @(
    "-c", $Configuration,
    "-r", $RuntimeIdentifier,
    "--self-contained", "true",
    "-p:Platform=$platform",
    "-p:WindowsPackageType=None",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:PublishDir=$publishDirectory"
)
Invoke-DotNet -Description "Publishing KoikatsuSceneGallery" -Arguments $appPublishArguments

$pluginsDirectory = Join-Path $publishDirectory "Plugins"
New-Item -ItemType Directory -Path $pluginsDirectory -Force | Out-Null

foreach ($plugin in $plugins) {
    if ($missingPlugins.Id -contains $plugin.Id) {
        Write-Warning "Skipping missing plugin $($plugin.Id)."
        continue
    }

    $repoPath = Join-Path $workspaceRoot $plugin.RepoDirectory
    $projectPath = Join-Path $repoPath $plugin.ProjectFile
    $pluginOutput = Join-Path $pluginsDirectory $plugin.Id
    New-Item -ItemType Directory -Path $pluginOutput -Force | Out-Null

    $pluginPublishArguments = @(
        "publish", $projectPath
    )
    $pluginPublishArguments += $restoreArguments
    $pluginPublishArguments += @(
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "false",
        "-p:Platform=$platform",
        "-p:DeployPluginToApp=false",
        "-p:SceneGalleryAppDir=$appRepo",
        "-o", $pluginOutput
    )
    Invoke-DotNet -Description "Publishing plugin $($plugin.Id)" -Arguments $pluginPublishArguments

    $pluginAssembly = Get-ChildItem -LiteralPath $pluginOutput -Filter "SceneGallery.Plugin.*.dll" -File
    if ($pluginAssembly.Count -ne 1) {
        throw "Expected exactly one plugin assembly in $pluginOutput; found $($pluginAssembly.Count)."
    }

    foreach ($sharedAssembly in @("SceneGallery.PluginSdk.dll", "KoikatsuSceneGallery.Core.dll")) {
        if (Test-Path (Join-Path $pluginOutput $sharedAssembly)) {
            throw "Plugin $($plugin.Id) duplicated shared assembly $sharedAssembly."
        }
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    configuration = $Configuration
    runtimeIdentifier = $RuntimeIdentifier
    application = $appState
    plugins = @($pluginStates)
}

$manifestPath = Join-Path $publishDirectory "plugins.manifest.json"
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Published application and plugins to: $publishDirectory"
Write-Host "Manifest: $manifestPath"

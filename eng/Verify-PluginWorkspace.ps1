#requires -Version 7.0
[CmdletBinding()]
param([string] $WorkspaceRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$validationRoot = Join-Path $repoRoot ('artifacts/plugin-workspace/' + [Guid]::NewGuid().ToString('N'))
$config = Join-Path $repoRoot 'eng/PackageValidation/NuGet.config'
$plugins = @('BepisDbPlugin', 'PixivAuthorsPlugin', 'FanboxWebView2Plugin', 'GitHubReleaseUpdatePlugin')
foreach ($plugin in $plugins) {
    if (!(Test-Path -LiteralPath (Join-Path $WorkspaceRoot "$plugin/eng/Verify-Plugin.ps1"))) { throw "Missing plugin checkout: $plugin" }
}
& (Join-Path $repoRoot 'scripts/Test-PluginPackages.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Source package validation failed.' }
& dotnet test (Join-Path $repoRoot 'PluginCommon.Tests/SceneGallery.PluginCommon.Tests.csproj') -c Release `
    --artifacts-path (Join-Path $validationRoot 'common') "-p:RestoreConfigFile=$config" `
    "-p:RestorePackagesPath=$(Join-Path $validationRoot 'common-packages')" --logger trx --results-directory (Join-Path $validationRoot 'common-results')
if ($LASTEXITCODE -ne 0) { throw 'Shared lifecycle tests failed.' }
& (Join-Path $repoRoot 'templates/MinimalPlugin/eng/Verify-Plugin.ps1') -NuGetConfig $config `
    -ArtifactsPath (Join-Path $validationRoot 'example') -PackagesPath (Join-Path $validationRoot 'example-packages')
if ($LASTEXITCODE -ne 0) { throw 'Example validation failed.' }
$assemblies = @()
foreach ($plugin in $plugins) {
    $path = Join-Path $WorkspaceRoot $plugin
    $artifacts = Join-Path $validationRoot $plugin
    & (Join-Path $path 'eng/Verify-Plugin.ps1') -NuGetConfig $config -ArtifactsPath $artifacts -PackagesPath (Join-Path $artifacts 'packages')
    if ($LASTEXITCODE -ne 0) { throw "$plugin validation failed." }
    $project = @(Get-ChildItem -LiteralPath $path -Filter 'SceneGallery.Plugin.*.csproj' -File)
    $metadata = (& dotnet msbuild $project[0].FullName -p:Configuration=Release -p:DeployPluginToApp=false `
        -p:UseArtifactsOutput=true "-p:ArtifactsPath=$artifacts" '-getProperty:TargetDir,AssemblyName') -join "`n" | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw "$plugin output evaluation failed." }
    $assemblies += Join-Path $metadata.Properties.TargetDir ($metadata.Properties.AssemblyName + '.dll')
}
& dotnet run --project (Join-Path $repoRoot 'eng/PluginLoadProbe/SceneGallery.PluginLoadProbe.csproj') -c Release `
    --artifacts-path (Join-Path $validationRoot 'loader') "-p:RestoreConfigFile=$config" -- @assemblies
if ($LASTEXITCODE -ne 0) { throw 'Production loader integration failed.' }
Write-Output "PASS: four plugins, shared lifecycle, source packages, example, and production loader. Evidence: $validationRoot"

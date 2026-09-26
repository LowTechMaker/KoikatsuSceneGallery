#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $Version,
    [string] $NuGetConfig,
    [string] $ArtifactsPath,
    [string] $PackagesPath
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$projects = @(Get-ChildItem -LiteralPath $root -Filter 'SceneGallery.Plugin.*.csproj' -File)
$testProjects = @(Get-ChildItem -LiteralPath $root -Directory | Where-Object Name -Like '*.Tests' |
    ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.csproj' -File })
if ($projects.Count -ne 1 -or $testProjects.Count -ne 1) { throw 'Expected one shipping project and one test project.' }
$properties = @('-p:DeployPluginToApp=false', "-p:Configuration=$Configuration")
if ($Version) { $properties += "-p:Version=$Version", '-p:IncludeSourceRevisionInInformationalVersion=false' }
if ($NuGetConfig) { $properties += "-p:RestoreConfigFile=$([IO.Path]::GetFullPath($NuGetConfig))" }
if ($PackagesPath) { $properties += "-p:RestorePackagesPath=$([IO.Path]::GetFullPath($PackagesPath))" }
if ($ArtifactsPath) { $properties += '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$([IO.Path]::GetFullPath($ArtifactsPath))" }
Push-Location $root
try {
    & dotnet test $testProjects[0].FullName @properties --nologo --logger trx
    if ($LASTEXITCODE -ne 0) { throw 'Plugin tests failed.' }
    $json = & dotnet msbuild $projects[0].FullName @properties '-getProperty:TargetDir,AssemblyName' '-getItem:PackageReference,ProjectReference,Compile'
    if ($LASTEXITCODE -ne 0) { throw 'MSBuild metadata evaluation failed.' }
    $metadata = ($json -join "`n") | ConvertFrom-Json
    if (@($metadata.Items.ProjectReference).Count) { throw 'Shipping plugins must consume contracts/packages, not sibling projects.' }
    $sdk = @($metadata.Items.PackageReference | Where-Object Identity -EQ 'SceneGallery.PluginSdk')
    if ($sdk.Count -ne 1 -or $sdk[0].Version -ne '1.3.0' -or $sdk[0].PrivateAssets -ne 'all' -or
        'runtime' -notin ($sdk[0].ExcludeAssets -split ';')) { throw 'SDK compile-only package contract changed.' }
    foreach ($package in $metadata.Items.PackageReference | Where-Object Identity -Like 'SceneGallery.PluginCommon*') {
        if ($package.Version -ne '0.2.0' -or $package.PrivateAssets -ne 'all') { throw "Invalid source package reference: $($package.Identity)" }
    }
    $prefix = [IO.Path]::GetFullPath($root).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    foreach ($source in $metadata.Items.Compile) {
        if (!$source.FullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and
            $source.NuGetPackageId -notin @('SceneGallery.PluginCommon', 'SceneGallery.PluginCommon.Secrets')) {
            throw "Unapproved external compile source: $($source.Identity)"
        }
    }
    $output = $metadata.Properties.TargetDir
    if (!(Test-Path -LiteralPath (Join-Path $output ($metadata.Properties.AssemblyName + '.dll')))) { throw 'Shipping output is missing.' }
    foreach ($name in @('SceneGallery.PluginSdk.dll', 'SceneGallery.PluginCommon.dll', 'SceneGallery.PluginCommon.Secrets.dll',
                         'NetArchTest.Rules.dll', 'Mono.Cecil.dll', 'KoikatsuSceneGallery.Core.dll')) {
        if (Get-ChildItem -LiteralPath $output -Filter $name -Recurse -File) { throw "Forbidden shipping dependency: $name" }
    }
    foreach ($document in @('AGENTS.md', 'ARCHITECTURE.md', 'HANDOFF.md')) {
        $path = Join-Path $root $document
        if (!(Test-Path -LiteralPath $path)) { throw "Missing $document" }
        $body = Get-Content -LiteralPath $path -Raw -Encoding utf8
        foreach ($match in [regex]::Matches($body, '\[[^\]]*\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value.Trim('<', '>').Split('#')[0]
            if (!$target -or $target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') { continue }
            if (!(Test-Path -LiteralPath (Join-Path $root ([Uri]::UnescapeDataString($target))))) {
                throw "Broken local link in ${document}: $target"
            }
        }
    }
    Write-Output "PASS: tests, contract dependencies, shipping output, and local documentation ($($metadata.Properties.AssemblyName))."
}
finally { Pop-Location }

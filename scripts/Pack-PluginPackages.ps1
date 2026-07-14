[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$SdkVersion = "1.0.0",
    [string]$CommonVersion = "0.1.0"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\local-feed"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$packConfigPath = Join-Path $repoRoot "eng\PackageValidation\NuGet.pack.config"
$restorePackagesPath = Join-Path $repoRoot "artifacts\package-restore"
$semverPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Get-NumericFileVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Version
    )

    if ($Version -notmatch $semverPattern) {
        throw "Package version '$Version' is not valid SemVer."
    }

    return "$($Matches[1]).$($Matches[2]).$($Matches[3]).0"
}

$sdkFileVersion = Get-NumericFileVersion -Version $SdkVersion
$null = Get-NumericFileVersion -Version $CommonVersion

function Invoke-PackagePack {
    param(
        [Parameter(Mandatory)]
        [string]$Project,
        [Parameter(Mandatory)]
        [string]$Version,
        [string]$FileVersion
    )

    & dotnet restore $Project `
        --configfile $packConfigPath `
        --nologo `
        -p:RestorePackagesPath=$restorePackagesPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet restore failed for $Project with exit code $LASTEXITCODE."
    }

    $packProperties = @(
        "-p:Version=$Version",
        "-p:IncludeSourceRevisionInInformationalVersion=false",
        "-p:RestorePackagesPath=$restorePackagesPath",
        "-p:TreatWarningsAsErrors=true"
    )
    if (-not [string]::IsNullOrWhiteSpace($FileVersion)) {
        $packProperties += "-p:FileVersion=$FileVersion"
    }

    & dotnet pack $Project `
        --configuration Release `
        --no-restore `
        --output $OutputDirectory `
        --nologo `
        @packProperties
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed for $Project with exit code $LASTEXITCODE."
    }
}

Invoke-PackagePack `
    -Project (Join-Path $repoRoot "PluginSdk\SceneGallery.PluginSdk.csproj") `
    -Version $SdkVersion `
    -FileVersion $sdkFileVersion
Invoke-PackagePack `
    -Project (Join-Path $repoRoot "PluginCommon\SceneGallery.PluginCommon.csproj") `
    -Version $CommonVersion
Invoke-PackagePack `
    -Project (Join-Path $repoRoot "PluginCommon.Secrets\SceneGallery.PluginCommon.Secrets.csproj") `
    -Version $CommonVersion

Write-Host "Plugin packages written to $OutputDirectory"

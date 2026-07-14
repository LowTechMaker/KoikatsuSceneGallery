[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$feedPath = Join-Path $repoRoot "artifacts\local-feed"
$validationRoot = Join-Path $repoRoot ("artifacts\package-validation\" + [Guid]::NewGuid().ToString("N"))
$packagesPath = Join-Path $validationRoot "packages"
$objPath = Join-Path $validationRoot "obj"
$binPath = Join-Path $validationRoot "bin"
$projectPath = Join-Path $repoRoot "eng\PackageValidation\SceneGallery.PluginPackages.Validation.csproj"
$configPath = Join-Path $repoRoot "eng\PackageValidation\NuGet.config"

& (Join-Path $PSScriptRoot "Pack-PluginPackages.ps1") -OutputDirectory $feedPath
if ($LASTEXITCODE -ne 0) {
    throw "Package packing failed with exit code $LASTEXITCODE."
}

& dotnet restore $projectPath `
    --configfile $configPath `
    --force-evaluate `
    --nologo `
    -p:RestorePackagesPath=$packagesPath `
    -p:BaseIntermediateOutputPath=$objPath\
if ($LASTEXITCODE -ne 0) {
    throw "Package validation restore failed with exit code $LASTEXITCODE."
}

& dotnet build $projectPath `
    --configuration Release `
    --no-restore `
    --nologo `
    -warnaserror `
    -p:RestorePackagesPath=$packagesPath `
    -p:BaseIntermediateOutputPath=$objPath\ `
    -p:OutputPath=$binPath\
if ($LASTEXITCODE -ne 0) {
    throw "Package validation build failed with exit code $LASTEXITCODE."
}

$assetsPath = Join-Path $objPath "project.assets.json"
if (-not (Test-Path -LiteralPath $assetsPath)) {
    throw "Package validation assets file was not generated at $assetsPath."
}

$assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
$target = $assets.targets.PSObject.Properties |
    Where-Object Name -Like "net10.0-windows*" |
    Select-Object -First 1
if ($null -eq $target) {
    throw "The validation assets file has no net10.0-windows target."
}

function Assert-CompileContentFiles {
    param(
        [Parameter(Mandatory)]
        [string]$PackageKey,
        [Parameter(Mandatory)]
        [int]$ExpectedCount
    )

    $package = $target.Value.PSObject.Properties[$PackageKey].Value
    if ($null -eq $package) {
        throw "Package $PackageKey is missing from the validation target graph."
    }

    $contentFiles = @($package.contentFiles.PSObject.Properties)
    if ($contentFiles.Count -ne $ExpectedCount) {
        throw "Package $PackageKey supplied $($contentFiles.Count) content files; expected $ExpectedCount."
    }

    foreach ($contentFile in $contentFiles) {
        if (-not $contentFile.Name.StartsWith("contentFiles/cs/any/", [StringComparison]::Ordinal)) {
            throw "Package $PackageKey used an unexpected contentFiles path: $($contentFile.Name)."
        }
        if ($contentFile.Value.buildAction -ne "Compile") {
            throw "Package $PackageKey content file $($contentFile.Name) has buildAction '$($contentFile.Value.buildAction)' instead of 'Compile'."
        }
    }
}

Assert-CompileContentFiles -PackageKey "SceneGallery.PluginCommon/0.1.0" -ExpectedCount 3
Assert-CompileContentFiles -PackageKey "SceneGallery.PluginCommon.Secrets/0.1.0" -ExpectedCount 1

$unexpectedRuntimeAssemblies = @(
    "SceneGallery.PluginSdk.dll",
    "SceneGallery.PluginCommon.dll",
    "SceneGallery.PluginCommon.Secrets.dll"
)
foreach ($assemblyName in $unexpectedRuntimeAssemblies) {
    if (Get-ChildItem -LiteralPath $binPath -Filter $assemblyName -Recurse -ErrorAction SilentlyContinue) {
        throw "Package validation output unexpectedly contains $assemblyName."
    }
}

Write-Host "Package validation succeeded."
Write-Host "contentFiles use contentFiles/cs/any with buildAction=Compile."
Write-Host "The validation project compiled with nullable enabled and warnings as errors."
Write-Host "No PluginSdk or PluginCommon runtime assembly was copied to the consumer output."

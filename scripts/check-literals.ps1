<#
.SYNOPSIS
  Fails when a value that has a home is written out at a call site again.

.DESCRIPTION
  The app keeps its values in one place per kind (user request, waves 1-3 of
  centralising values):
    motion            Controls/CardMotion.cs (TagCloudMotion.cs for the tag cloud)
    waits / timeouts  KoikatsuSceneGallery.Core/Helpers/AppTiming.cs
    limits            KoikatsuSceneGallery.Core/Helpers/AppLimits.cs
    file names        KoikatsuSceneGallery.Core/Services/AppFiles.cs
    sizes / spacing   Controls/UiMetrics.cs and Styles/Tokens.xaml
    colours           Styles/Tokens.xaml (or a system brush)
    icons             Helpers/Glyphs.cs and the Glyph* strings in Tokens.xaml
    rating labels     KoikatsuSceneGallery.Core/Helpers/RatingLabels.cs
    on-screen text    Strings/*/Resources.resw
  This script catches the three slips that are easy to spot mechanically:
    1. a raw private-use character (an icon) in C# - use Glyphs.<Name>;
    2. TimeSpan.FromMilliseconds/FromSeconds/FromMinutes with a number in it
       outside the motion and timing homes - add a named value to AppTiming
       (or CardMotion for an animation);
    3. a hex colour outside Tokens.xaml - add a token or use a system brush.
  Tests, plugins and generated code are not checked.

.EXAMPLE
  pwsh scripts/check-literals.ps1      # exit 0 = clean, 1 = findings listed
#>
[CmdletBinding()]
param([string]$Root = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'

# Folders that are not the app's own code.
$skipDirs = '\\(obj|bin|artifacts|docs|\.trail|\.git|PluginCommon|PluginCommon\.Tests|KoikatsuSceneGallery\.Tests|KoikatsuSceneGallery\.IntegrationTests|SceneGallery\.PluginSdk)\\'

# Files that own their numbers, each documented where it is.
$timeOwners = @(
    'Controls\CardMotion.cs',
    'Controls\TagCloudMotion.cs',
    'KoikatsuSceneGallery.Core\Helpers\AppTiming.cs',
    'Helpers\AuthorTileLayout.cs'          # the layout-burst guard; see its remarks
)
$colourOwners = @(
    'Styles\Tokens.xaml',
    'Helpers\PlatformAccent.cs',           # the platforms' brand colours
    'Helpers\HtmlDescriptionRenderer.cs'   # parses colours out of post HTML
)

function Get-Sources([string[]]$Extensions) {
    Get-ChildItem -Path $Root -Recurse -File -Include $Extensions |
        Where-Object { $_.FullName -notmatch $skipDirs }
}

function Get-Relative([string]$Path) { $Path.Substring($Root.Length).TrimStart('\', '/') }

$findings = [System.Collections.Generic.List[string]]::new()
$pua = '[' + [char]0xE000 + '-' + [char]0xF8FF + ']'
$timeLiteral = 'TimeSpan\.From(Milliseconds|Seconds|Minutes)\(\s*[0-9]'
$hexColour = '#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?\b'

foreach ($file in Get-Sources @('*.cs')) {
    $rel = Get-Relative $file.FullName
    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match $pua) {
            $findings.Add("${rel}:$($i + 1): raw icon character - use Glyphs.<Name>")
        }
        if ($timeOwners -notcontains $rel -and $line -match $timeLiteral -and $line.TrimStart() -notmatch '^//') {
            $findings.Add("${rel}:$($i + 1): time written as a number - name it in AppTiming (or CardMotion)")
        }
        if ($colourOwners -notcontains $rel -and $line -match $hexColour -and $line.TrimStart() -notmatch '^//') {
            $findings.Add("${rel}:$($i + 1): hex colour - add a token in Styles/Tokens.xaml or use a system brush")
        }
    }
}

foreach ($file in Get-Sources @('*.xaml')) {
    $rel = Get-Relative $file.FullName
    if ($colourOwners -contains $rel) { continue }
    $lines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $hexColour) {
            $findings.Add("${rel}:$($i + 1): hex colour - add a token in Styles/Tokens.xaml or use a system brush")
        }
    }
}

if ($findings.Count -eq 0) {
    Write-Output 'check-literals: clean'
    exit 0
}
$findings | ForEach-Object { Write-Output $_ }
Write-Output "check-literals: $($findings.Count) finding(s)"
exit 1

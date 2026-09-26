#requires -Version 7.0
[CmdletBinding()]
param([string] $RepositoryRoot = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot)
$indexFile = Join-Path $root 'CLAUDE.md'
$directory = Join-Path $root 'docs/handoffs'
$index = Get-Content -LiteralPath $indexFile -Raw -Encoding UTF8
$handoffs = @(Get-ChildItem -LiteralPath $directory -Filter 'phase-*.md' -File)
if ($handoffs.Count -eq 0) { throw 'No handoff documents found.' }

function Without-CodeFences([string] $Text) {
    [regex]::Replace($Text, '(?ms)^```[^\r\n]*\r?\n.*?^```[^\r\n]*\r?$', '')
}

$indexBody = Without-CodeFences $index
$pending = 0
$read = 0
foreach ($file in $handoffs) {
    $relative = 'docs/handoffs/' + $file.Name
    if (!$indexBody.Contains("]($relative)", [StringComparison]::Ordinal)) {
        throw "Missing direct CLAUDE.md link: $relative"
    }
    $body = Without-CodeFences (Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8)
    $version = [regex]::Match($body, '(?m)^- 交接版本：v([1-9]\d*)\s*$')
    if (!$version.Success) { throw "Missing handoff version: $relative" }
    $markers = [regex]::Matches($body,
        '(?m)^- \[(?<state>[ xX])\] Claude 已讀 v(?<version>[1-9]\d*)（閱讀完成時間：(?<time>[^；\r\n]+)；閱讀者／任務：(?<reader>[^）\r\n]+)）\s*$')
    $current = @($markers | Where-Object { $_.Groups['version'].Value -eq $version.Groups[1].Value })
    if ($current.Count -ne 1) { throw "Expected one current-version Claude marker: $relative" }
    foreach ($marker in $markers) {
        if ($marker.Groups['state'].Value -eq ' ') { continue }
        $time = $marker.Groups['time'].Value.Trim()
        $reader = $marker.Groups['reader'].Value.Trim()
        $parsed = [DateTimeOffset]::MinValue
        if ($time -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$' -or
            ![DateTimeOffset]::TryParse($time, [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None, [ref] $parsed) -or !$reader -or $reader -match '待填') {
            throw "Read marker needs a timestamp with timezone and reader/task: $relative"
        }
    }
    if ($current[0].Groups['state'].Value -eq ' ') { $pending++ } else { $read++ }
}

$linkCount = 0
$documents = @((Get-Item -LiteralPath $indexFile)) + @(Get-ChildItem -LiteralPath $directory -Filter '*.md' -File)
foreach ($file in $documents) {
    $body = Without-CodeFences (Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8)
    foreach ($match in [regex]::Matches($body, '\[[^\]]*\]\(([^)]+)\)')) {
        $target = $match.Groups[1].Value.Trim('<', '>').Split('#')[0]
        if (!$target -or $target -match '^(https?://|mailto:)') { continue }
        $resolved = Join-Path $file.DirectoryName ([Uri]::UnescapeDataString($target))
        if (!(Test-Path -LiteralPath $resolved)) { throw "Broken link in $($file.Name): $target" }
        $linkCount++
    }
}

Write-Output "PASS: $($handoffs.Count) handoffs; current markers pending=$pending, read=$read; $linkCount local links resolved."

#requires -Version 7.0
# 把「單一」交接目前版本的 Claude 已讀欄改為 [x]，填入現在時間（含時區）與閱讀者。
# 只能在實際讀完整份文檔後執行。一次只接受一個檔案、不接受萬用字元，避免批次假標。
# 目前版本已勾選、找不到唯一的目前版本欄位時拒絕執行；失敗時必須向使用者回報「尚未補標記」。
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Path,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Reader
)

$ErrorActionPreference = 'Stop'
if ($Path -match '[\*\?\[\]]') { throw 'Pass exactly one handoff path; wildcards are not allowed.' }
if ($Reader -match '待填|[；）\r\n]') { throw 'Reader must be a real reader/task id without ；, ） or newlines.' }
$file = Get-Item -LiteralPath $Path
if ($file.Name -notmatch '^phase-\d+\.md$') { throw "Not a handoff document: $($file.Name)" }

$utf8 = [Text.UTF8Encoding]::new($false)
$text = [IO.File]::ReadAllText($file.FullName, $utf8)
$version = [regex]::Match($text, '(?m)^- 交接版本：v([1-9]\d*)\s*$')
if (!$version.Success) { throw "Missing handoff version: $($file.Name)" }
$v = $version.Groups[1].Value

$pattern = "(?m)^- \[(?<state>[ xX])\] Claude 已讀 v$v（閱讀完成時間：[^；\r\n]+；閱讀者／任務：[^）\r\n]+）[ \t]*(?=\r?$)"
$markers = [regex]::Matches($text, $pattern)
if ($markers.Count -ne 1) { throw "Expected one Claude marker for v$v in $($file.Name), found $($markers.Count)." }
$marker = $markers[0]
if ($marker.Groups['state'].Value -ne ' ') { throw "v$v of $($file.Name) is already marked read; nothing changed." }

$time = [DateTimeOffset]::Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", [Globalization.CultureInfo]::InvariantCulture)
$line = "- [x] Claude 已讀 v$v（閱讀完成時間：$time；閱讀者／任務：$Reader）"
$updated = $text.Remove($marker.Index, $marker.Length).Insert($marker.Index, $line)
[IO.File]::WriteAllText($file.FullName, $updated, $utf8)
Write-Output "Marked $($file.Name) v$v read at $time by $Reader."

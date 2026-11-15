#requires -Version 7.0
# 建立新交接骨架（未讀欄維持 [ ]）並把索引列插到 docs/handoffs/INDEX.md 表格最上面。
# 已存在同編號的檔案或索引列時拒絕執行，不覆寫。
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateRange(1, 9999)] [int] $Number,
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Title,
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
)

$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$name = 'phase-{0:d2}.md' -f $Number
$directory = Join-Path $RepositoryRoot 'docs/handoffs'
$path = Join-Path $directory $name
$indexPath = Join-Path $directory 'INDEX.md'

if (Test-Path -LiteralPath $path) { throw "Already exists: docs/handoffs/$name" }
$index = [IO.File]::ReadAllText($indexPath, $utf8)
if ($index.Contains("]($name)", [StringComparison]::Ordinal)) { throw "INDEX.md already links $name" }

$separator = "| --- | --- |"
$at = $index.IndexOf($separator, [StringComparison]::Ordinal)
if ($at -lt 0) { throw 'INDEX.md table separator not found.' }
$newline = $index.Contains("`r`n") ? "`r`n" : "`n"
$lineEnd = $index.IndexOf("`n", $at) + 1
$row = "| $Number | [$Title]($name) |$newline"

$date = (Get-Date).ToString('yyyy-MM-dd')
$body = @"
# 第 $Number 部分交接：$Title

- 交接版本：v1
- 日期：$date
- 完成範圍：

## 改動

## 涉及檔案

## 保留政策

## 驗證

## 請使用者手動確認

## 尚未完成

## 下一步

## Claude 已讀標記

- [ ] Claude 已讀 v1（閱讀完成時間：待填；閱讀者／任務：待填）
"@ -replace "`r?`n", $newline

[IO.File]::WriteAllText($path, $body + $newline, $utf8)
[IO.File]::WriteAllText($indexPath, $index.Insert($lineEnd, $row), $utf8)
Write-Output "Created docs/handoffs/$name and added it to INDEX.md. Fill every section before verifying."

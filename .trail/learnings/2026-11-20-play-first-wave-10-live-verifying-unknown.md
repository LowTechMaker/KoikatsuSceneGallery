---
title: "Play-first wave 10: live verifying the wrapped home shelves"
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 8, tokens: 91351 }
touches:
  - docs/play-first/wave-10/verify-live-report.md
---

## Decided
Home shelf checks use Tile<i> rectangles and Tile ancestors, not HomeShelf_<key>_Row: the row is a plain panel and is not in the UIA tree. Scroll each shelf header into view with ScrollItemPattern first, because off-screen tiles report infinite rectangles. Helper: live\h10-measure.ps1. TransformPattern resize to 1920x1080 works. AcceleratorKey (Q, L, H) shows on the flag buttons after SetFocus on the tile.
## Tried and ruled out
Opening a tile's context menu: only IUIAutomationElement3.ShowContextMenu does it, which needs a hand-written COM interop that PowerShell cannot do. It was not driven, so menu items were not verified.
## Watch out
The gallery_temp file count baseline is now 10222, not the old 10117. Take the count before the round and compare against that.

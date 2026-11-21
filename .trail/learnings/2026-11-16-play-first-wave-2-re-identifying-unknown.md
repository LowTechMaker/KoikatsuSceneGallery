---
title: "Play-first wave 2: re-identifying library cards, live-test harness lessons"
author: Unknown
date: 2026-11-16
branch: experiment/play-first
---

## Decided
- A library card gets its source later through LibraryCardReidentifier (app) -> LibraryCardRelocation + LibraryCardMover (Core). Order: hash the card, move, write the post sidecar, re-point rating and card state by hash, invalidate the import file cache. Steps after the move never undo it.
- One place decides import paths now: ImportDestinationPolicy.ResolveTarget (Core). ImportService.ResolveDestinations and the re-identify path both call it. Wave 3 (simpler Organized layout) should change only that function.
- Authorless imports go to Unknown/<game>/!unrecognized so GalleryGrouping gives each file its own tile. Existing files are not moved.
- CardStateReconciler re-attaches state to cards moved or renamed outside the app; an entry only counts as orphaned if its root still has at least one live card.
- CardOriginSelection.NoSource filters to cards without an author; the library flyout has the switch.

## Tried and ruled out
- Re-feeding a library file through the import page: classified AlreadyInLibrary, and source == destination or the symlink conflict choice deletes the card.
- Reusing CreateManualArtworkId in the dialog: it assigns any text to the first provider, so a guess would move the card.
- Reusing ArtworkFileAssignmentService.Move: batch, throws, no cross-volume copy path. Its rollback style was copied instead.

## Watch out
- Live harness: do NOT set Language in the swapped config. The unpackaged language override throws in OnLaunched before the line that applies CacheFolderPath, so thumbnails go to the user's real gallery_temp.
- Live harness: never leave a live run open across a pause. A usage limit hit mid-run left the user's settings pointing at the fake library for 7.5 hours. Restore before any long wait.
- A ContentDialog left open blocks CloseMainWindow; the test process then has to be killed.
- UI Automation patterns (Invoke/Toggle/SelectionItem/SetFocus) plus PrintWindow with PW_RENDERFULLCONTENT drive and capture the app without touching mouse, keyboard or foreground. Shelf flag buttons only enter the UIA tree after SetFocus on the tile.
- The actual file move of re-identify was never run live (needs the user's account to fetch a post). Only unit tests cover it.
- Re-identify moves one card; import would also gather the post's loose siblings into the artwork folder.

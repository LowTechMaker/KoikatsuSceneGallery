---
title: "Play-first wave 3: the three-level library layout and moving an existing library"
author: Unknown
date: 2026-11-16
branch: experiment/play-first
cost: { minutes: 72, tokens: 1709488 }
touches:
  - Controls/LayoutMigrationDialog.cs
  - KoikatsuSceneGallery.Core/Services/LibraryLayoutMigration.cs
  - KoikatsuSceneGallery.Tests/LibraryLayoutMigrationTests.cs
  - Services/LibraryLayoutMigrationService.cs
  - Strings/en-US/Resources.resw
  - Strings/zh-Hans/Resources.resw
  - Strings/zh-Hant/Resources.resw
  - App.xaml.cs
  - KoikatsuSceneGallery.Core/Helpers/SettingsSearchIndex.cs
  - KoikatsuSceneGallery.Core/Services/CardStateStore.cs
---

## Decided
- Library layout is `Organized\<provider>\<author (id)>\<artwork (id)>\`; game and rating are no longer folder levels. `ImportDestinationPolicy.ResolveTarget` is the only place that builds a path; new layout = game "" and rating "".
- The rating folder was the only rating source for cards without a post sidecar, so the rating is stored as `CardState.FiledRating` and `SceneRating.FolderOrFiled` takes the higher of folder and filed. Anything that moves a card out of a rating folder must file the rating first and must not move the card if that fails.
- Old-layout author folders are still found and reused (same rating only), so an unmigrated library keeps working and both layouts coexist.
- `LibraryLayoutMigration` moves an existing library: plan first, ratings on disk before the first move, write-ahead JSONL journal per root in `<root>\.scenegallery\layout_migration_<ts>.json`, per-folder documents (post sidecars, characters.json, source.json) merged after the moves from backups, undo offered whenever the dialog opens.

## Tried and ruled out
- Dropping the provider level: `AuthorInfoService.GetCandidateProviders` needs the provider folder to pick the plugin that parses `name (id)`.
- `SidecarBackupStore` for migration backups: it is keyed by provider for refetch; originals are kept beside the journal instead.
- Hashing every file into the journal: reads the whole library twice; length + write time identify a move in flight.

## Watch out
- Live runs load no plugins on purpose (a loaded plugin would fetch fake author ids with the user's account), so only Local and Unknown provider folders get a live check.
- A fixture meant to be "identical bytes" must be a file copy; generating two images with the same seed text still differed in colour and was a collision.
- Agents write new files with LF; normalise to CRLF before committing.
- An undo button that only exists on the result page is lost when the dialog closes: anything reversible needs to be reachable from a cold start.

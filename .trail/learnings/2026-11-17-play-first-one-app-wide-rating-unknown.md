---
title: "Play-first: one app-wide rating filter, and a start that reuses the card cache"
author: Unknown
date: 2026-11-17
branch: experiment/play-first
cost: { minutes: 123, tokens: 3218016 }
touches:
  - KoikatsuSceneGallery.Core/Helpers/CardScanTracker.cs
  - KoikatsuSceneGallery.Tests/CardScanTrackerTests.cs
  - MainWindow.xaml
  - MainWindow.xaml.cs
  - Services/CardScanService.cs
  - ViewModels/GalleryViewModel.cs
  - Controls/LibraryBrowser.xaml.cs
  - KoikatsuSceneGallery.Core/Helpers/CardCacheReuse.cs
  - KoikatsuSceneGallery.Core/Helpers/HomeCardCounts.cs
  - KoikatsuSceneGallery.Core/Helpers/StartupTimingLog.cs
---

## Decided
- The scene rating filter is one persisted app-wide value (`SettingsViewModel.RatingFilter`, `RatingFilterEnabled`), pushed into `GalleryViewModel.RatingFilter` by `MainWindow` like the browse origin. `RatingFilterRule.Passes` (Core) is the only place that decides; gallery, scoped browses, random discovery and the home page all ask it. The rating page does not filter.
- With a filter on, the home page shows no card whose rating is not settled yet (`RatingsSettled` / `IsRatingSettled`), so nothing appears and then vanishes.
- A start reuses the scene card cache: a listed file with the same path, size and write time is not opened (`CardCacheReuse`) and is dropped in the scan worker without reaching the UI thread (`CardScanTracker`, which still records it as seen so removals work). The cache is written only when it changed; thumbnails are checked by listing their folder once.
- Ratings are read as soon as the cached cards are shown, alongside the scan; `SceneRatingResolver.ReloadAsync` drops a read that finishes after a later one started.
- `startup_timing.log` in the app's data folder: one line per phase per library per start, names and numbers only. It is how timings from the user's own library are obtained without reading that library.

## Tried and ruled out
- Deferring the character and coordinate galleries until first opened: the author index needs all three libraries at start.
- Blaming the scene scan's batches for the character/coordinate scans being ~4x slower on a warm start: taking those batches off the UI thread did not change them.
- Blaming a replaced items panel for the title bar rating row not being selectable through UI Automation: restoring the panel did not fix it.

## Watch out
- The title bar rating row (a toolkit `Segmented` in `TitleBar.Content`) reports as a UIA Group whose items only support ScrollItem: it cannot be selected or read back through UI Automation. The origin switch is built the same way and probably has the same defect.
- The character and coordinate galleries have no card cache: every start opens every file.
- A file whose size changed with an unchanged write time is now re-read; before, the stale cached card was kept.
- A live run on a fake library writes `startup_timing.log` into the app's data folder; read it before restoring, and check none is left behind.

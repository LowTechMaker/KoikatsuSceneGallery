---
title: "Scene rating filter: tri-state All/R-18 only/G only"
author: Unknown
date: 2026-11-03
branch: local-collection-and-card-versions
cost: { minutes: 3, tokens: 104660 }
touches:
  - AGENTS.md
  - Controls/LibraryBrowser.xaml
  - Controls/LibraryBrowser.xaml.cs
  - Helpers/SceneDiscoveryFilter.cs
  - KoikatsuSceneGallery.Core/Helpers/GalleryGrouping.cs
  - KoikatsuSceneGallery.Core/Services/ImportExecutionTransaction.cs
  - KoikatsuSceneGallery.Core/Services/ImportTransactionExecutor.cs
  - KoikatsuSceneGallery.IntegrationTests/ImportServiceTests.cs
  - KoikatsuSceneGallery.Tests/GalleryGroupingTests.cs
  - KoikatsuSceneGallery.Tests/ImportExecutionTransactionTests.cs
---

## Decided
- Scene gallery rating filter is `RatingFilterOption { All, R18Only, GeneralOnly }` on `GalleryViewModel.RatingFilter` (replaced bool `ShowR18Content`). Used by `SceneDiscoveryFilter`, LibraryBrowser filter panel (ComboBox built in code) and RandomDiscoveryPage flyout (ComboBox in XAML). Labels shared via `LibraryBrowser.RatingFilterLabels()`.
- "G" = all ages (not R-18G), matching `GalleryEntry.RatingText`.

## Tried and ruled out
- Separate R-18G option: not possible without changing `SceneCard.IsR18Content` (bool; true for both R-18 and R-18G folders) into a three-state rating.

## Watch out
- `RandomDiscoveryPage` has its own default `_filter` initializer (field) besides `CaptureFilter`; both must change when the `SceneDiscoveryFilter` signature changes.
- A C# compile error in the app shows up together with a misleading XAML `WMC9999` null-reference error; fix the C# error first.

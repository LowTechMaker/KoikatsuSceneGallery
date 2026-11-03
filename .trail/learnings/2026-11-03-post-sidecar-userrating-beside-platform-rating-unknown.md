---
title: Post sidecar userRating beside platform rating
author: Unknown
date: 2026-11-03
branch: local-collection-and-card-versions
cost: { minutes: 4, tokens: 33504 }
touches:
  - AGENTS.md
  - Controls/LibraryBrowser.xaml
  - Controls/LibraryBrowser.xaml.cs
  - Helpers/SceneDiscoveryFilter.cs
  - KoikatsuSceneGallery.Core/Helpers/GalleryGrouping.cs
  - KoikatsuSceneGallery.Core/Models/PostMetadataDocument.cs
  - KoikatsuSceneGallery.Core/Services/ImportExecutionTransaction.cs
  - KoikatsuSceneGallery.Core/Services/ImportTransactionExecutor.cs
  - KoikatsuSceneGallery.Core/Services/PostMetadataStore.cs
  - KoikatsuSceneGallery.IntegrationTests/ImportServiceTests.cs
---

## Decided
- Platform rating stays in PostMetadataDocument.Rating (re-fetch overwrites it); the user's manual choice is a separate optional `UserRating` (int?, same 0/1/2 scale). `EffectiveRating` = UserRating ?? Rating, marked [JsonIgnore].
- Import writes UserRating only when the review rating differs from the fetched one (`PostMetadataMapper.ToImportDocument`), so untouched cards follow the platform.
- PostMetadataStore merge: `incoming.UserRating ?? existing.UserRating`; a fetch (null) never clears it. No SchemaVersion bump (optional field).

## Tried and ruled out
- SQLite as the store: does not fix the real gaps (manual rating not written, sources without ratings, card→post key); sidecars travel with the library folder. At most a rebuildable index later.

## Watch out
- Before this, the review rating only chose the rating folder; sidecars held only FetchedArtworkInfo.Rating.
- Two import paths write sidecars: ImportService.SaveFetchedMetadataAsync and ImportViewModel.BuildExecutionPlansFromWorkspaceAsync (via ImportTransactionExecutor). Change both.
- Rollback on a pre-existing sidecar only unbinds file names, so a UserRating written by that import remains.
- Card→post key must go through CardGroupKey.For (provider parsers), as TagCloudService does.

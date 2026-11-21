---
title: "Online import: same card in two folders rolled the batch back"
author: Unknown
date: 2026-10-22
branch: local-collection-and-card-versions
cost: { minutes: 6, tokens: 96626 }
touches:
  - AGENTS.md
  - Controls/LibraryBrowser.xaml
  - KoikatsuSceneGallery.Core/Helpers/GalleryGrouping.cs
  - KoikatsuSceneGallery.Core/Services/ImportExecutionTransaction.cs
  - KoikatsuSceneGallery.Core/Services/ImportTransactionExecutor.cs
  - KoikatsuSceneGallery.Tests/GalleryGroupingTests.cs
  - KoikatsuSceneGallery.Tests/ImportExecutionTransactionTests.cs
  - KoikatsuSceneGallery.Tests/ImportReviewWorkspaceTests.cs
  - Pages/ImportPage.xaml
  - Pages/ImportPage.xaml.cs
---

## Decided
- Online import plans (ImportViewModel.BuildExecutionPlansFromWorkspaceAsync) now resolve in-batch destination clashes with LocalDestinationNames.Resolve, only when a destination is already claimed by an earlier item in the batch.
- "Same card" = byte-identical OR CardPayloadHash.AreSameCard (card data after the PNG; preview ignored). Same card keeps the shared destination and the executor folds it as a duplicate; a different card gets `_1`, `_2`...
- ImportItemPlan.MatchCardData (init) lets the executor's TryClassifyConflict accept card-data equality as Duplicate. Online plans set it true; local plans do not.

## Tried and ruled out
- Renaming library clashes (destination already on disk with a different card) for online import: left unchanged on purpose. It still rolls back; phase 97 lists it as the user's decision.

## Watch out
- ImportTransactionExecutor used to compare whole files byte for byte, while ImportLibraryIndexer already used CardPayloadHash for copy-suffixed names. The two definitions of "duplicate" disagreed, and that mismatch was the root cause.
- With MatchCardData, a library file holding the same card under a different preview now counts as a duplicate, and the online source gets deleted (KeepDuplicateSource=false).
- The plan builder reads files, so keep it off the UI thread. Snapshot the ImportItem properties before Task.Run.

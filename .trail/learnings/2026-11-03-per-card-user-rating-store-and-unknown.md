---
title: Per-card user rating store and rating page
author: Unknown
date: 2026-11-03
branch: local-collection-and-card-versions
cost: { minutes: 19, tokens: 108454 }
touches:
  - AGENTS.md
  - App.xaml.cs
  - Controls/LibraryBrowser.xaml
  - Controls/LibraryBrowser.xaml.cs
  - Helpers/SceneDiscoveryFilter.cs
  - KoikatsuSceneGallery.Core/Helpers/GalleryGrouping.cs
  - KoikatsuSceneGallery.Core/Models/PostMetadataDocument.cs
  - KoikatsuSceneGallery.Core/Services/ImportExecutionTransaction.cs
  - KoikatsuSceneGallery.Core/Services/ImportTransactionExecutor.cs
  - KoikatsuSceneGallery.Core/Services/PostMetadataStore.cs
---

## Decided
- The user's own per-card rating lives in UserCardRatingStore (Core): one `<scene root>/.scenegallery/user_ratings.json` per root, keyed by CardPayloadHash, with relativePath as a fast lookup. Separate from post sidecar `rating` (platform) and `userRating` (import override, phase 102).
- Rating page (CardRatingPage, nav Tag "rating") shows only unrated scene cards; DetailPage has a "My rating" block. Both use Controls/UserRatingPicker.

## Tried and ruled out
- Storing it in the post sidecar: one sidecar per post means all cards of a post would share a rating, and local cards have no sidecar.

## Watch out
- Unreadable rating file → moved aside as `.unreadable-<ts>`; locked file → root becomes read-only. Never overwrite a file you could not read.
- Snapshot entries inside PathWriteLock, or two quick ratings can land out of order.
- FindRoot must append a separator before StartsWith, or `library-2` matches root `library`.
- UserRatingPicker is a Grid, not a Control: use SetEnabled, not IsEnabled.
- Large python heredocs in the Bash tool can fail to parse; write the script to the scratchpad and run it.

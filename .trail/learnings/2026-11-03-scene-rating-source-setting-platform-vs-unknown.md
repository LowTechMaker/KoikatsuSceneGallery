---
title: "Scene rating source setting: platform vs user"
author: Unknown
date: 2026-11-03
branch: local-collection-and-card-versions
cost: { minutes: 8, tokens: 73182 }
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
- SceneCard.Rating (0/1/2) is the one rating the gallery filters and labels by; IsR18Content is derived. Rules in Core Helpers/SceneRating: platform = post sidecar `rating`, else rating folder; user = UserCardRatingStore, else platform.
- SceneRatingResolver builds a `post:{provider}:{id}` → rating map from sidecars after each gallery load (via PostSidecarLocator, shared with TagCloudService) and matches cards through CardGroupKey.For.
- Setting `UseUserRatings` (SettingsViewModel.RatingSourceIndex 0/1). RatingFilterOption has five values in combo-index order: All, GeneralOnly, R18AndR18G, R18Only, R18GOnly.

## Tried and ruled out
- Reading one sidecar per card or hashing every card at load: too slow; map lookups plus path lookups instead.

## Watch out
- Cards show folder ratings until ReloadRatingsAsync finishes; newly added cards keep folder ratings until the next load.
- BepisDB sidecars always say G and Fanbox has no R-18G, so "platform" mode can differ from the folder; that is intended.
- GalleryViewModel ctor is internal now (takes the internal resolver).

---
title: Play-first workflow redesign plan (with SceneFeed)
author: Unknown
date: 2026-11-15
branch: local-collection-and-card-versions
cost: { minutes: 3, tokens: 41235 }
touches:
  - App.xaml
  - Controls/LibraryBrowser.xaml
  - Controls/LibraryBrowser.xaml.cs
  - Controls/UserRatingPicker.cs
  - Helpers/DetailNavigationHelper.cs
  - Helpers/DragFilePayload.cs
  - Helpers/ViewerChrome.cs
  - Helpers/VisualTreeSearch.cs
  - KoikatsuSceneGallery.Core/Helpers/CardOriginNavPolicy.cs
  - KoikatsuSceneGallery.Tests/CardOriginNavPolicyTests.cs
---

## Decided
- User said the tool became too complex and killed their interest in applying scene cards; asked for a redesigned flow combining SceneFeed, breaking changes allowed, plan only. Plan is docs/play-first/PLAN.md, handoff phase-121. No code changed; user decides stages later.
- Direction: from "file cabinet" to "play table". Cards arrive without review (Gallery watches Feed Downloads, identifies via .scenefeed/handoff.json), home page of shelves (new / want to try / deal a hand / recent + favourites), one main action "send to Studio", card relationship states (new, queued, used, liked, hidden) stored like UserCardRatingStore.
- Nav shrinks from 9 items + 2 switches to 4 (home, library, inbox, settings). Recommended start: stage 1 (states + home) and stage 2 (send to Studio), both non-breaking.
- Feed integration stays loosely coupled: manifest + CLI only, no assembly references.

## Tried and ruled out
- Continuing the UI/UX overhaul proposal as the answer: it improves each page but does not reduce the number of pages, steps or decisions.
- Direct "load scene in Studio" as the first approach: plugin capability not verified, so a fixed "today" folder under UserData/Studio/scene comes first.

## Watch out
- Root cause found: classification lives in the file path (ImportDestinationPolicy.BuildTargetBase: import subfolder/provider/game/rating/author/artwork), which is why import needs review and conflicts need dialogs.
- Not verified: whether GalleryGrouping and the author index derive ownership from folder names (blocks stages 3 and 6).
- The plan overturns decisions from parts 73, 74, 75, 77, 81, 82, 85, 89, 92, 103, 107, 111, 119, 120; entries are removed, capabilities kept. Seven open questions for the user are in PLAN.md section 9.
- Feed CLI has sync and single-post get only; "get all pending" and a blocked-items listing would need adding, under Feed's own rules (no agent commits, platform isolation).
- h5i is not available on this machine; session-start script reports it and continues.

---
title: "Play-first wave 1: card states, home shelves, worktree gotchas"
author: Unknown
date: 2026-11-15
branch: experiment/play-first
cost: { minutes: 21, tokens: 231830 }
touches:
  - App.xaml.cs
  - Controls/CardActionBar.cs
  - Controls/CardShelf.cs
  - Controls/LibraryBrowser.CardMenu.cs
  - Controls/LibraryBrowser.FilterChips.cs
  - Controls/LibraryBrowser.xaml
  - Helpers/CardFileActions.cs
  - Helpers/CardStateActions.cs
  - Helpers/DetailNavigationHelper.cs
  - Helpers/DragFilePayload.cs
---

## Decided
- Experiment lives on branch experiment/play-first in a sibling worktree (SceneGallery/KoikatsuSceneGallery-playfirst); it must sit beside the plugin repos because eng/LocalPlugins.targets resolves them with ..\..
- Card "relationship" state (queued / liked / hidden / last used) is CardStateStore in Core: one <root>/.scenegallery/card_states.json per library root, keyed by payload hash, modelled on UserCardRatingStore but with an additive EnsureLoaded. CardStateService is the only app-side entry; CardStateActions is the shared toggle+toast.
- "Used" is recorded at the existing drag/copy funnels regardless of record mode; UsageRecordService is unchanged.
- Hidden cards are filtered in GalleryViewModelBase.OriginPasses, the one predicate every gallery and scoped browser already calls; each gallery's "no filter" fast path also checks HasHiddenFilter.
- Home shelves are not virtualized (max 30 cards); tiles request thumbnails when EffectiveViewportChanged puts them near the viewport.
- User's facts: finding cards is the tiring part; Studio's scene window is only used for the scene root; Organized is never browsed, simplifying it is wave 3; unidentified cards may enter the library but must be identifiable later (wave 2).

## Tried and ruled out
- Sending cards to a dedicated folder for Studio: dropped from the experiment, the user finds cards in the gallery and drags them.
- A "new arrivals" shelf from DateCreated: file creation time survives a same-volume move, so it is not import time.
- Letting agents write helper scripts to the user's Temp folder: rejected; they use obj/agent-tmp inside the repo.

## Watch out
- docs/* is git-ignored (except three plugin docs): plans and handoffs are not in git and do not appear in a worktree. Handoffs stay in the main checkout's docs/handoffs.
- A fresh worktree lacks ignored local files: Properties/PublishProfiles/*.pubxml (without them the integration tests fail with NETSDK1150) and artifacts/local-feed (plugins do not deploy; build with -p:BuildLocalPlugins=false or point LocalPluginFeedDir at the main checkout).
- Repo files are CRLF. A bash heredoc piped to python and `grep -c $'\r'` both mislead here; patch with a script file that normalises CRLF on read and restores it on write.
- Do not pass -p:Platform=x64 to dotnet test on the integration project; the project references set it themselves.
- Live UI verification of wave 1 was NOT done: the user's own KoikatsuSceneGallery.exe was running and the live-test rule forbids swapping settings then.

---
title: "Play-first home page: this-or-that panel, tile captions, daily themed shelves"
author: Unknown
date: 2026-11-16
branch: experiment/play-first
cost: { minutes: 151, tokens: 4331417 }
touches:
  - KoikatsuSceneGallery.Core/Helpers/HomeThemes.cs
  - KoikatsuSceneGallery.Tests/HomeThemesTests.cs
  - Controls/CardShelf.cs
  - Controls/DuelPanel.cs
  - Controls/HomeCardCaption.cs
  - KoikatsuSceneGallery.Core/Helpers/DuelLayout.cs
  - KoikatsuSceneGallery.Tests/DuelLayoutTests.cs
  - Pages/HomePage.xaml.cs
  - Strings/en-US/Resources.resw
  - Strings/zh-Hans/Resources.resw
---

## Decided
- Home tiles are captioned with post title and author. Titles and tags come from the post sidecars `SceneRatingResolver` already reads once per load off the UI thread; no extra file reads per tile.
- `HomeDuel` (Core) deals pairs for the this-or-that panel; `DuelLayout.Fit` caps the panel at half the window height.
- `HomeThemes.Pick` (Core) chooses up to three themed shelves per day from a seed of date + reroll counter + library. It sorts its input by path first: the scene list comes from a parallel scan and arrives in a different order every launch.
- "Never played" is offered only when at most half the library is unplayed; on a mostly unplayed library it was just another random hand and blocked other themes through the overlap rule.
- Division of work: builds and test runs go to the opencode agent, live verification to the Opus agent; the lead reads reports and re-checks that the user's settings were restored.

## Tried and ruled out
- "Looks like the last one you played" as a wired theme: library cards have no image fingerprints (they are computed only for files being imported and not kept), so it would need a full decode of every scene card plus a new cache.
- "See all" on a tag shelf: nothing in the app opens the tag page on a given tag.
- `AccessibilityView.Raw` on a caption panel to hide its text lines from UI Automation: it does not hide children; set it on each line.

## Watch out
- Anything date-seeded must not depend on the order cards load in, nor on `string.GetHashCode` (per-process).
- Do not commit with `git add -A` while another agent is writing tests-first files in the same tree: its half-done test file went into a commit once and had to be taken out.
- Live screenshots come out black when the display is off or the session is locked; measure with UI Automation bounding rectangles and say the picture was not seen.
- Home shelves, the duel and themes do not apply the gallery's rating filter.

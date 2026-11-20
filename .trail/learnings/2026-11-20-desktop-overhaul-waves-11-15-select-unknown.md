---
title: "Desktop overhaul waves 11-15: select + inspector + in-place loupe, one card viewer"
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 30, tokens: 212898 }
touches:
  - Controls/CardInspector.cs
  - Controls/CardLoupe.cs
  - Controls/CharacterVersionsPanel.cs
  - Controls/LibraryBrowser.Selection.cs
  - Controls/UserRatingPicker.cs
  - Helpers/BackNavigation.cs
  - Helpers/CardKeys.cs
  - Helpers/GalleryLayoutEngine.cs
  - Helpers/ViewerChrome.cs
  - Models/BrowseContext.cs
---

## Decided
- Click selects, double click / Enter opens in place (CardLoupe over the grid); the details pane (CardInspector) sits beside the grid in LibraryBrowser and on HomePage. Pages with no grid open CardViewerPage (loupe + pane). The three detail pages, their view models and ViewerChrome were deleted; DetailNavigationHelper stays (integration tests use it).
- Card keys live in CardKeys (Q/F/X, 1-3, IsModifierDown, IsTyping). Rating read/save in CardRatingActions. Character info, versions and annotation in CharacterVersionsPanel.
- Every corner radius is a resource (Control/Overlay/Surface/Large/MediaTile/Badge); the "square corners" setting zeroes them in App.ApplyCornerStyle before MainWindow exists (restart needed). Circles keep literal radii.
- Cold start: cached scene cards are made off the UI thread and added in two deferrals (first 200, then the rest); the rating resolver saves scene_posts.json (post key -> rating, title, tags) and the next start settles from it at once.

## Tried and ruled out
- KeyDown on the loupe for its arrows: focus stayed on the grid behind it, so arrows moved the hidden grid selection. The host's tunnelling PreviewKeyDown routes loupe keys while it is open.
- Hiding overflow tiles inside WrapPanel: they stayed focusable; ShelfLines decides how many tiles are put in the tree.

## Watch out
- XAML compiler WMC9999 (NullReferenceException) can hide plain C# errors (here CS0136); build with DOTNET_CLI_UI_LANGUAGE=en and --no-incremental to see them. That env var also adds a harmless CS2038 warning.
- grunt (opencode) cannot read the lead's scratchpad without a permission prompt; send task text inline. Do not answer its permission prompts for it; ask the user.
- Python edits must keep CRLF in .resw files: write bytes, not text with newline=''.
- GalleryLayoutEngine: the items panel can have no width when a tab first opens; it falls back to the grid's width or a default size.

---
title: "Waves 16-17: five-entry pane, loupe redesign, author panes; live-check lessons"
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 30, tokens: 149908 }
touches:
  - Controls/AuthorInspector.cs
  - Pages/AuthorsPage.xaml
  - Pages/AuthorsPage.xaml.cs
  - Strings/en-US/Resources.resw
  - Strings/zh-Hans/Resources.resw
  - Strings/zh-Hant/Resources.resw
  - docs/play-first/wave-16/verify-live.md
  - Controls/CardInspector.cs
  - Controls/LibraryBrowser.Selection.cs
  - Controls/LibraryBrowser.xaml
---

## Decided
- Pane: home, library, authors, import, settings. Discovery, rating, tags, usage live in the library's "More" menu (GalleryMoreButton) and Ctrl+K; local sources are a button on the authors page, and "authors" opens them when no plugin lists authors. MainWindow.NavigateToSection(tag) opens any section; the old nav items stay collapsed as targets. Record mode shows as "Recording" in the title bar.
- Loupe (CardLoupe): header = Back + position + zoom + pane toggle (the pane carries the title); no box; fit stops at 2x the card's pixels; wheel zooms at the pointer, drag pans when zoomed, double click toggles fit / close-up. In the library it covers the whole content column and the toolbar, search row and tab heading are hidden while it is open.
- Authors page selects then opens (AuthorInspector beside the grid); author overview sections wrap onto two lines (ShelfLines).

## Tried and ruled out
- Tile rectangles relative to the loupe for the open/close flight: the host moves the loupe (hides its toolbar) right after measuring. Use window coordinates (TransformToVisual(null)) on both sides and restore the host layout (CardLoupe.Closing) before measuring for the flight home.
- A pure C# Page: Frame.Navigate to it is an access violation in coreclr (not in crash.log; Windows event log, .NET Runtime 1026). Every page needs a XAML file.

## Watch out
- GridView's own selected background fills the whole cell; turn it off through GridViewItemBackgroundSelected* resources when the tile draws its own mark.
- Focus a tile with FocusState.Programmatic after closing an overlay, or the keyboard focus frame shows.
- GalleryLayoutEngine: never size tiles from a width under 200 px (a transient measure); a replaced items panel must be re-sized.
- Composition StopAnimation("Translation") throws unless translation was enabled on that element; enable it up front.
- Live checks cannot see popups or sub-second animations (PrintWindow, ~0.4 s per shot); author pages cannot be checked live (no plugins).

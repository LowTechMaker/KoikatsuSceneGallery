---
title: Local plugins in a worktree / x64 build; UiMetrics and CardMotion as the one place for values
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 44, tokens: 183943 }
touches:
  - Controls/AuthorInspector.cs
  - Controls/CardInspector.cs
  - Controls/CardLoupe.cs
  - Controls/CardShelf.cs
  - Controls/LibraryBrowser.Selection.cs
  - Controls/LibraryBrowser.xaml
  - Controls/UiMetrics.cs
  - Models/GalleryEntry.cs
  - Pages/AuthorsPage.xaml.cs
  - Pages/CardViewerPage.xaml.cs
---

## Decided
- Sizes and spacing of the new UI live in Controls/UiMetrics.cs (templates read them with x:Bind); motion tempos in Controls/CardMotion.cs; colours and corners in Styles/Tokens.xaml. New hard-coded numbers go there, not in controls.
- Card mode (file names on) uses MediaCardBackgroundBrush / MediaCardStrokeBrush (Tokens.xaml) for a card fill clearly apart from the page.

## Tried and ruled out
- Expecting local plugins in a Debug x64 run: the plugin repos deploy to bin\<Config>\<TFM>\<Rid>\Plugins (no platform folder) while Platform=x64 runs the app from bin\x64\<Config>\...; eng/LocalPlugins.targets now copies them over.

## Watch out
- A git worktree has no artifacts\local-feed (ignored): copy it from the main checkout, or every sibling plugin repo fails to restore and SGPLUG001 says "nothing landed".
- With plugins deployed in the Debug bin, live UI runs must move bin\x64\Debug\...\Plugins aside first (the harness rule is: no plugins, nothing online).

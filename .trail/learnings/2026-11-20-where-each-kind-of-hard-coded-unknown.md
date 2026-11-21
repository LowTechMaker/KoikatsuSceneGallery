---
title: "Where each kind of hard-coded value lives (centralised values, waves 1-4)"
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 80, tokens: 705448 }
touches:
  - scripts/check-literals.ps1
  - Controls/AuthorInspector.cs
  - Controls/AuthorLiveTileControl.xaml
  - Controls/CardActionBar.cs
  - Controls/CardInspector.cs
  - Controls/CardLoupe.cs
  - Controls/CardMenu.cs
  - Controls/CharacterVersionsPanel.cs
  - Controls/Filmstrip.cs
  - Controls/ImportDropZoneContent.xaml
---

## Decided
- One home per kind of value; call sites use the name, never the number:
  motion -> Controls/CardMotion.cs (tag cloud: TagCloudMotion.cs); waits, debounces, timeouts, toast times -> Core/Helpers/AppTiming.cs; worker counts, batch sizes, caps, similarity thresholds -> Core/Helpers/AppLimits.cs; file and folder names -> Core/Services/AppFiles.cs (a test checks they are distinct); sizes and breakpoints in code -> Controls/UiMetrics.cs; spacing scale Space4/8/12/16, IconSize, RingSize, text styles, colours -> Styles/Tokens.xaml; icons -> Helpers/Glyphs.cs plus the Glyph* x:String resources in Tokens.xaml (generated from Glyphs.cs, keep in step); G / R-18 / R-18G -> Core/Helpers/RatingLabels.cs (not resw: same in every language); on-screen text -> resw (label: value goes through Format_LabelValue).
- One icon per meaning: queue = Pin E718, hide = Hide ED1A, import = Download E896, author = Contact E77B, character = Emoji2 E76E (no longer the author's icon).
- Disk-bound parallel work uses AppLimits.IoWorkers = clamp(cores - 1, 1, 4) everywhere.
- scripts/check-literals.ps1 fails on a raw private-use char in C#, a TimeSpan.From*(number) outside the timing/motion homes, or a hex colour outside Tokens.xaml. Run it with the build.

## Tried and ruled out
- Writing "" through the Write tool: it stores the raw character, not the escape. Write glyphs through a script that emits chr(92)+'u'+hex, or let the check script catch it.
- Heredoc-fed Python with backslashes in the Bash tool: the backslashes arrive mangled; put the script in a file.

## Watch out
- Some limits stay with their documented owner and are only listed in AppLimits' remarks: ArtworkRefetchPolicy, HomeThemes, PokerHand, ArchiveExtractor, MetadataValueReader, AuthorTileLayout's burst guard, PlatformAccent brand colours.
- Settings search index (Core/Helpers/SettingsSearchIndex.cs) must move with a settings card when it changes tab.
- The test project compiles some app files by link (Models/ImportItem.cs, ViewModels/ImportReviewRow.cs), so Helpers/Glyphs.cs is linked into KoikatsuSceneGallery.Tests too.

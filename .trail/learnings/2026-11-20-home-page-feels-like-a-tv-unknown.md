---
title: "Home page feels like a TV streaming app: causes and WinUI 3 options"
author: Unknown
date: 2026-11-20
branch: experiment/play-first
cost: { minutes: 3, tokens: 40374 }
---

## Decided
- Nothing implemented yet; the user is choosing a direction. Diagnosis: the "TV app" feel comes from layout, not WinUI 3. Causes: horizontal-scroll shelves (CardShelf uses a horizontal ScrollViewer, needs OnWheel to remap the wheel), a hero-sized this-or-that panel (DuelLayout.Fit allows up to half the window height), click = open, hover-only tile actions with no toolbar or multi-select, and low density (SubtitleTextBlockStyle headings, 24px spacing, 135px tiles).
- Options proposed, cheapest and highest impact first: (1) wrap shelves with ItemsView + LinedFlowLayout/UniformGridLayout, 1-2 lines plus "show all"; (2) shrink the duel to a normal-height section or a side column; (3) a right-hand details pane where click selects and double-click/Enter opens; (4) Ctrl/Shift multi-select, a CommandBar and a right-click MenuFlyout (reuse LibraryBrowser.CardMenu); (5) denser typography and spacing plus a tile-size slider; (6) KeyboardAccelerators.

## Tried and ruled out
- Switching frameworks: not needed, since WinUI 3 has all of these controls.

## Watch out
- Changing click = open to click = select is a change in how the user interacts with the app; ask before doing it.
- An earlier learning says home shelves and the duel ignore the rating filter, but commit ba8721a may have changed that. Check before adding the duel refill (deal more cards when fewer than two pass the filter).

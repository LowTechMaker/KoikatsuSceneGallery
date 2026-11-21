---
title: UI/UX overhaul audit and proposal
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 4, tokens: 128336 }
touches:
  - ACCEPTANCE.md
  - COMMON-CODE.md
  - IMPORT-REFACTOR.md
  - METADATA.md
  - animate_test/\345\234\226\347\211\207-000.jpeg
  - animate_test/\345\234\226\347\211\207-001.jpeg
  - animate_test/\345\234\226\347\211\207-002.jpeg
  - animate_test/\345\234\226\347\211\207-003.jpeg
  - animate_test/\345\234\226\347\211\207-004.jpeg
  - animate_test/\345\234\226\347\211\207-005.jpeg
---

## Decided
- User asked for a UI/UX overhaul covering shell/nav, library browsing, detail pages, import and settings; chose "audit first, then propose". Pain points: discoverability, tedious flows, perf/feedback.
- Audit + 5-stage proposal (0 shared tokens/PageHeader/toast, 1 shell, 2 library, 3 detail, 4 import+settings) lives in docs/ui-overhaul/PROPOSAL.md; handoff phase-109. No code changed yet; 5 open decisions await the user.

## Tried and ruled out
- Nothing implemented. Proposal keeps design decisions recorded in XAML comments (count beside sort, single sort DropDownButton, Segmented for 3 values, platform SelectorBar only when there is a choice).

## Watch out
- Scene/Character/Coordinate detail pages are near-duplicate XAML that have drifted (scene has user rating but no MetadataPanel; character the reverse).
- Filters are split across LibraryBrowser flyout, Settings > Filters (resolution lists) and RandomDiscoveryPage's own set.
- Detail pages have an in-page back button on top of the TitleBar back button.
- Many AutomationIds are used by UI tests; keep them when merging pages.
- Python printing zh-Hant strings on this machine needs PYTHONIOENCODING=utf-8 (console is cp932).

---
title: Handoff rules moved from CLAUDE.md into the handoffs skill
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 4, tokens: 71618 }
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
- CLAUDE.md stays short: required reading, the non-negotiable rules, and a pointer to `.claude/skills/handoffs/`. Core decisions, safety limits and failure policy are in SKILL.md; the format and verifier details are in references/; repeatable steps are in scripts/ (New-Handoff.ps1, Set-HandoffRead.ps1).
- The handoff index moved to docs/handoffs/INDEX.md with relative links `](phase-NN.md)`. eng/Verify-Handoffs.ps1 now reads INDEX.md and also checks links in the skill's Markdown.
- .gitignore changed from `.claude/` to `.claude/*` + `!.claude/skills/` so the skill is committed.
## Watch out
- Don't add new handoff rows back into CLAUDE.md. Use New-Handoff.ps1, which inserts the row at the top of the INDEX.md table.
- Set-HandoffRead.ps1 takes one file only and rejects wildcards and files already marked read. Run it only after reading the whole document.

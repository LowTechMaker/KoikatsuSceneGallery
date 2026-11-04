---
title: Zipmod install from the import page
author: Unknown
date: 2026-11-03
branch: local-collection-and-card-versions
cost: { minutes: 14, tokens: 197394 }
touches:
  - Pages/ImportPage.xaml
  - Pages/ImportPage.xaml.cs
  - Pages/SettingsPage.xaml
  - Services/SettingsService.cs
  - Strings/en-US/Resources.resw
  - Strings/zh-Hans/Resources.resw
  - Strings/zh-Hant/Resources.resw
  - ViewModels/ImportViewModel.cs
  - ViewModels/SettingsViewModel.cs
  - ACCEPTANCE.md
---

## Decided
Zipmods are not gallery items. ImportViewModel.StageModsAsync collects .zipmod/.zip with a GUID manifest into PendingMods; nothing is written until the user confirms the ImportPage install dialog. Writes go only to <game>\mods\<ModInstallFolderName> (default SceneGallery). Planning/IO lives in Core ModInstallation.cs (ModInstallPlanner pure, ModInstaller writes .sgtmp then renames, logs to mod-installs.jsonl).
Older copy of same GUID: backed up (outside mods) only if inside our folder; elsewhere left alone — Sideloader loads the newest.
Post link checks treat installed or pending zipmod names as "already have" via TryGetLibraryLookupAsync, instead of writing zipmods into post sidecars.
## Tried and ruled out
Recording mods in the post sidecar: needs to know which post a mod belongs to, and does nothing for posts imported earlier.
## Watch out
SettingsViewModel.SaveConfigAsync rebuilds ConfigData from VM fields: any new ConfigData field must be loaded and saved by the VM or it is wiped on the next save.
KKS loading Koikatsu-tagged mods and Sideloader picking the newest duplicate GUID are assumed, not tested in the real game.

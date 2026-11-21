---
title: "Live UI testing on the user's desktop: what is safe"
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 30, tokens: 695983 }
touches:
  - App.xaml
  - Controls/LibraryBrowser.xaml
  - Controls/LibraryBrowser.xaml.cs
  - Helpers/DetailNavigationHelper.cs
  - Helpers/DragFilePayload.cs
  - Helpers/ViewerChrome.cs
  - Helpers/VisualTreeSearch.cs
  - KoikatsuSceneGallery.Core/Helpers/CardOriginNavPolicy.cs
  - KoikatsuSceneGallery.Tests/CardOriginNavPolicyTests.cs
  - MainWindow.xaml
---

## Decided
- Live runs swap %LOCALAPPDATA%\KoikatsuSceneGallery settings for a fake library: back up every top-level file, move the card caches out, point FolderPaths/CharacterFolderPaths/CoordinateFolderPaths AND CacheFolderPath at scratchpad folders, restore afterwards. Refuse to start if KoikatsuSceneGallery.exe is running.
- Before any screenshot: the foreground window must belong to the app's process AND WindowFromPoint at the capture's four corners and centre must belong to the app. Before any key press: foreground must be the app's.
- Read crash.log (diff against the size saved before the run) BEFORE restoring; restoring overwrites it.

## Tried and ruled out
- Checking only "foreground window belongs to the app": a flyout of the app can be foreground while another program's window covers the main window; the capture then contains the other program (it happened twice, capturing a billing page).
- Simulating an Explorer drag with WinForms DoDragDrop + synthetic mouse moves: the moves and the release act on whatever is on the real desktop. Do not test drag-drop this way; ask the user to try it by hand.
- Shift+F10 after UIA SetFocus on a GridViewItem does not open the tile's ContextFlyout; a single right-click at the item centre, after checking both foreground and WindowFromPoint belong to the app, does.
- Forcing the app to the foreground while the user is working in another app (foreground lock): don't; mark the check unverified instead.

## Watch out
- The thumbnail cache is the user's real one unless CacheFolderPath is redirected; earlier runs left 184 fake-card thumbnails there that had to be deleted by timestamp.
- The user's AllowedResolutions contains 320x180, the size of the generated fake cards, so a resolution-filter test with them filters nothing.
- A custom Panel (Controls/WrapPanel) has no automation peer; its children appear under the parent in UIA, so AutomationId on the panel finds nothing.

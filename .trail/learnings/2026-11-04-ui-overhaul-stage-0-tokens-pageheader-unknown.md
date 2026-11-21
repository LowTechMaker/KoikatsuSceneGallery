---
title: "UI overhaul stage 0: tokens, PageHeader, AppToast"
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 19, tokens: 149869 }
touches:
  - App.xaml
  - Controls/LibraryBrowser.xaml
  - Helpers/DetailNavigationHelper.cs
  - MainWindow.xaml
  - MainWindow.xaml.cs
  - Pages/AuthorDetailPage.xaml
  - Pages/AuthorsPage.xaml
  - Pages/CardRatingPage.xaml
  - Pages/CardRatingPage.xaml.cs
  - Pages/DetailPage.xaml.cs
---

## Decided
- Shared design tokens live in Styles/Tokens.xaml, merged in App.xaml after XamlControlsResources. Badge colours over thumbnails use MediaOverlayBadge* brushes (stay dark in light theme; HC variants exist). Group count keeps the lighter #CC202020 from phase 108.
- Controls/PageHeader (Title/Subtitle/Actions) is the page title row. Localise via x:Uid with keys ending .Title/.Subtitle; a key ending .Text on a PageHeader's Uid would target a missing property.
- Helpers/AppToast is for events (copied, failed to save), not state. Same `key` replaces the previous toast. DetailNavigationHelper.CopyText always toasts.
- Bottom-right of MainWindow is one column: ToastHost above RefetchBannerHost.

## Tried and ruled out
- Putting ResourceDictionary.ThemeDictionaries between plain resources: XamlCompiler WMC0035 "Duplication assignment to the '_Items' property". Keep ThemeDictionaries first.

## Watch out
- Live runs: back up %LOCALAPPDATA%\KoikatsuSceneGallery, move the card caches out, point FolderPaths/CharacterFolderPaths/CoordinateFolderPaths at fake folders, restore afterwards. Read crash.log BEFORE restoring - restoring overwrites what the run logged.
- PluginUpdate.Check logs a JsonException on every start (FanboxWebView2Plugin update source returns HTML); pre-existing, not a UI regression.
- An empty StackPanel (ToastHost) is not in the UIA tree; find toasts by their InfoBar instead.
- trail check flags LibraryBrowser.xaml:96 (phase 108 comment) as "one card every"; false positive.

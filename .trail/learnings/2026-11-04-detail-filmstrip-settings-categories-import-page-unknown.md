---
title: "Detail filmstrip, settings categories, import page merge"
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 27, tokens: 362022 }
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
Detail filmstrip lives in ViewerChrome (Controls/Filmstrip), shared by the three detail pages; policy in Core FilmstripPolicy. Inside a drilled-down folder, BrowseContext.Outer carries the gallery's top-level list (GalleryGrouping order) so the strip shows cards past the folder edge, darkened by CardGroupKey; clicking one opens it with the outer context.
Settings page: Pivot replaced by left category ListView + SettingsTab (ContentControl with Header, so Settings_Tab*.Header x:Uid strings still apply); below 720px a ComboBox replaces the list. SettingsSearchIndexTests splits tabs on `<appControls:SettingsTab`.
Import page: one InfoBar for failure/rejected/result (ImportPagePolicy.OpenMessages order), StepIndicator, selection actions as a CommandBar of AppBarButtons.
## Tried and ruled out
Requesting thumbnails outside the gallery with the default (Prefetch) priority or without ActivateThumbnailRequests(): LibraryBrowser deactivation calls CancelPendingWork(), which cancels the thumbnail session, and every later request is refused silently.
Driving the app's file picker in live tests: it opens on the user's Documents (screenshots capture their folder names) and UIA CheckBox searches hit the dialog's PreviewButton, flipping a shell-wide preference. Do not use the picker; there is no non-drag way to fill the import list.
## Watch out
A Button whose content is a Border around an Image centres its content by default: with no image source yet the Border measures 0x0 and the tile vanishes. Set Horizontal/VerticalContentAlignment = Stretch.
Clearing and re-adding the same elements to a panel can deliver the old Unloaded after the new Loaded; guard Unloaded with VisualTreeHelper.GetParent(this) is not null, and avoid rebuilding when the sequence is unchanged.
scratchpad safe_ui.ps1 probe arrays need each coordinate in parentheses: in PowerShell `@($a + 1, $b + 1)` parses the comma before the +.

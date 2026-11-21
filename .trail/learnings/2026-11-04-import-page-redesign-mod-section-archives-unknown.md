---
title: "Import page redesign: mod section, archives, conflict dialog"
author: Unknown
date: 2026-11-04
branch: local-collection-and-card-versions
cost: { minutes: 30, tokens: 366668 }
touches:
  - Controls/ImportReviewTemplateSelector.cs
  - KoikatsuSceneGallery.Core/Helpers/ImportReviewLayout.cs
  - KoikatsuSceneGallery.Core/Services/ImportLibraryIndexer.cs
  - KoikatsuSceneGallery.Core/Services/ImportReviewPolicy.cs
  - KoikatsuSceneGallery.Tests/ImportReviewLayoutTests.cs
  - KoikatsuSceneGallery.Tests/ImportReviewWorkspaceTests.cs
  - KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj
  - Models/ImportItem.cs
  - Pages/ImportPage.xaml
  - Pages/ImportPage.xaml.cs
---

## Decided
Import list sections (ImportReviewLayout): Unidentified, Unavailable, Identified, Mods, Skipped (collapsed by default); empty sections hidden; cards always one row each (the identified grid was dropped). Mods are ModReviewItem rows laid out by ImportReviewWorkspace.SetMods; their box means "install", a card's box means "select for editing".
Name clash with a different card in the library no longer rolls the batch back: BuildExecutionPlansFromWorkspaceAsync holds it back (ImportConflictRules.IsNameClash), the rest imports, then ImportAllAsync asks via ResolveConflicts (page dialog): rename / overwrite (old file moved aside, recycled after success, restored on failure) / skip. Same card already elsewhere in the library (ImportItem.IsInLibraryElsewhere, from ImportLibraryIndexResult.ExistingLibraryPaths): skip / hard link / symbolic link / copy.
Non-mod .zip archives are unpacked by ArchiveExtractor into LocalFolder/import-staging (deleted on clear and at startup); the archive itself is never touched.
File picker uses Microsoft.Windows.Storage.Pickers.FileOpenPicker (WinAppSDK).
## Tried and ruled out
Repurposing ImportItemStatus.Skipped for "user skipped": Skipped already means "needs manual repair" (ImportReviewPolicy test ManualIdentificationMovesAnUnknownCardIntoReview); a separate UserSkipped flag is used instead.
Windows.Storage.Pickers.FileOpenPicker: PickMultipleFilesAsync throws COMException 0x80004005 when several long-path files are picked.
## Watch out
x:Uid applies every resource with that prefix: reusing a Button's Uid (e.g. Import_Options with .Content) on a TextBlock crashes at load; new label Uids were made for that.
Writing XAML with both the Write tool and a manual BOM prefix produced a double BOM and WMC9999 "root level data invalid".
AlreadyInLibrary cards now also get a DestinationPath (needed to know "elsewhere"); a copy below the destination folder (post subfolder) counts as the same place.

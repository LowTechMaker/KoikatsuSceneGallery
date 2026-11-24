---
title: Dependabot NuGet group PR misses XamlTestHost and other test csprojs
author: Unknown
date: 2026-11-24
branch: master
cost: { minutes: 1, tokens: 2865 }
touches:
  - ".tmp-fast/CHANGELOG.md"
  - ".tmp-fast/README.md"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000000.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000004.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000008.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000012.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000016.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000020.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000024.png"
  - ".tmp-fast/corpus/gallery/root00/folder000/card000028.png"
---

## Decided
- Not yet decided whether to merge Dependabot PR #23 (7 NuGet bumps). Recommendation given: replace it with a manual branch split into (1) test tooling and (2) WindowsAppSDK/WebView2/BuildTools/WinApp, then close #23.

## Tried and ruled out
- Merging #23 as-is: CI build fails with NU1605 (WindowsAppSDK downgrade 2.7.0 -> 2.4.0) because Dependabot only edits KoikatsuSceneGallery.csproj and KoikatsuSceneGallery.Tests.csproj.

## Watch out
- KoikatsuSceneGallery.IntegrationTests/XamlHost/KoikatsuSceneGallery.XamlTestHost.csproj pins its own Microsoft.WindowsAppSDK and CommunityToolkit.WinUI.Collections; bump them together with the main csproj or restore fails.
- Microsoft.NET.Test.Sdk / xunit.runner.visualstudio are pinned separately in IntegrationTests, PluginCommon.Tests and templates/MinimalPlugin tests; keep them aligned.
- The vstest CVE-2026-105544 note in the PR affects Microsoft.TestPlatform.CLI (shipped in the .NET SDK); fix is an SDK update, not the Test.Sdk NuGet.
- Microsoft.Windows.SDK.BuildTools.WinApp 0.3.1 -> 0.8.0 is a big pre-1.0 jump; verify the unpackaged publish zip after bumping.
- The dependabot.yml "*" group bundles app runtime and test tooling into one PR; consider splitting WindowsAppSDK/WinApp out.

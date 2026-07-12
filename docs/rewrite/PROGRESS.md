# Staged refactor progress

## Task 1 — Test safety net and Core library

## Result: Completed

## Commands run

`rtk test dotnet test` completed after authorized NuGet restore. The test assembly was discovered and all 56 tests passed.

`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --no-restore` completed successfully after the RID-specific restore. The app, Core, and PluginSdk built with 0 warnings and 0 errors.

Earlier no-restore attempts correctly failed with `NETSDK1004`/`NETSDK1047` before the required assets existed. The first compiled app attempt found inaccessible internal Core helpers; this was fixed without widening public API by granting friend-assembly access, after which both acceptance commands were rerun successfully.

## Tests: 56 total / 56 passed / 0 failed / 0 skipped

## Changes

Added the plain `net10.0` Core and xUnit test projects, a solution containing the app and its dependencies, synthetic characterization fixtures, fixed WebView2 versioning, and Windows plus Ubuntu CI test coverage. Pure helpers, parsers, classifiers, metadata models, and `ResolutionOption` moved to Core with their namespaces and behavior preserved.

## Notes / out-of-scope findings

The ignored local Avalonia tree had generated sources that polluted the root SDK glob, so the root project now excludes that tree without modifying it. A root solution makes an unqualified solution-level RID build invalid (`NETSDK1134`); the acceptance and CI build therefore name the app project explicitly while retaining the required Release, x64, and win-x64 values. Details are in `NOTES.md`.

## Risks

The dedicated Ubuntu CI job has not run locally. PluginSdk public API, JSON schema, MessagePack schema, cache naming, and user data paths were not changed.

## Task 2 — Remove the App static Service Locator

## Result: Completed

## Commands run

`rtk test dotnet test --no-restore` passed all 56 tests.

`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --no-restore` completed with 0 warnings and 0 errors.

`rtk proxy dotnet publish KoikatsuSceneGallery.csproj -c Release -r win-x64 --self-contained --no-restore -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -p:PublishDir=publish\smoke\` completed successfully in the product's unpackaged self-contained distribution mode.

The published `KoikatsuSceneGallery.exe` was launched in the interactive desktop session. UI automation found the `Koikatsu Scene Gallery` window, `LibraryNavItem`, `GallerySearchBox`, sorting/filter controls, and multiple loaded `SceneCard` items. After temporary startup diagnostics were removed, the app was republished; the process launched and remained responsive. A repeat UI-automation call was not run because the desktop-action approval service reported exhausted workspace credits.

Static searches returned zero `App.` references in `ViewModels/`, `Services/`, and `Helpers/`; `App.xaml.cs` exposes only the single `AppServiceRegistry Services` static entry.

The earlier packaged `dotnet run` attempt was intentionally superseded by the required unpackaged smoke path; the product ships as a self-contained zip and packaged registration conflicts are irrelevant to that distribution mode.

## Tests: 56 total / 56 passed / 0 failed / 0 skipped

## Changes

Replaced public static service/ViewModel properties with a single registry entry, added constructor injection throughout ViewModels and services, preserved keyed screenshot/video instances and optional import/post lifetimes, and moved author-source orchestration to `AuthorSourceCoordinator`.

## Notes / out-of-scope findings

The smoke publish initially attempted restore and failed because the sandbox cannot read the roaming `NuGet.Config`; rerunning the same publish with `--no-restore` used the already-restored win-x64 assets and succeeded. No package registration or user data was changed.

## Risks

The composition path has now been exercised through the unpackaged shipping form and loaded an existing Gallery successfully. The repeat post-diagnostic UI inspection was unavailable due tool-credit exhaustion, but the diagnostic changes were fully reverted and the resulting source passed the acceptance test/build commands before republishing.

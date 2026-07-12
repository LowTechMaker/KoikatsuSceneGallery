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

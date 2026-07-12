# Rewrite notes

## Task 1 out-of-scope findings

- The ignored local `KoikatsuSceneGallery.Avalonia` directory contains generated `obj` and build output. Before Task 1, the root WinUI project recursively compiled that generated source and failed with duplicate assembly attributes. Task 1 excludes the entire local directory through `DefaultItemExcludes`; no Avalonia files were changed or deleted.
- `BuildAndRun.ps1` is not present in the repository. Task 1 uses the `dotnet build` acceptance path from `AGENTS.md`.
- The first sandboxed baseline build could not read `C:\Users\alexc\AppData\Roaming\NuGet\NuGet.Config`. An approved retry reached compilation and exposed the unrelated Avalonia glob issue. A later approval request for solution restore/build was rejected because the workspace approval system reported insufficient credits; unaffected implementation work continued.
- A `dotnet test --no-restore` attempt initially returned success without discovering tests because the generated xUnit template relies on restored test SDK props to mark the project as a test project. Task 1 now sets `IsTestProject` explicitly; no no-op test command is counted as verification.
- With `IsTestProject` explicit, no-restore validation correctly failed with `NETSDK1004` for the missing Core/Tests `project.assets.json`. A later authorized restore completed, and the real test run passed 56/56.
- After adding the required root solution, an unqualified `dotnet build -p:RuntimeIdentifier=win-x64` selects the solution and fails with `NETSDK1134` because solution-level RID builds are unsupported. CI and final app verification therefore specify `KoikatsuSceneGallery.csproj` while retaining the same configuration, platform, and runtime identifier.

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

## Task 3 — Error handling and async hygiene

## Result: Completed

## Commands run

`rtk test dotnet test --no-restore` passed all 56 tests at the stage start and again after the final changes.

`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --no-restore` completed with 0 warnings and 0 errors.

Static searches for empty catch blocks, `async void`, and discarded `Task.Run`/async method calls each returned zero results. `rtk git diff --check` also completed with no whitespace errors.

## Tests: 56 total / 56 passed / 0 failed / 0 skipped

## Changes

Added the injected `IAppLogger`/`CrashLogLogger` abstraction and registered it at the composition root. Empty catches now log operation and path/provider context while retaining their original control flow. Added `TaskExtensions.Observe` for fire-and-forget work and `UiEventGuard` for XAML/override async handlers; non-event `async void` card handlers and drag helpers now return `Task` and are explicitly observed.

## Notes / out-of-scope findings

No new out-of-scope findings were added. The existing static `CrashLog` remains only as the private storage backend used by `CrashLogLogger`; application services, ViewModels, Pages, and Controls depend on `IAppLogger`.

## Risks

Expected cancellation paths are now logged as well as swallowed, preserving control flow at the cost of additional diagnostic entries during rapid navigation. Task 5 will refine cancellation ownership and propagation without changing the Task 3 observability contract.

## Task 4 — Split the import flow

## Result: Completed

## Commands run

`rtk test dotnet test --no-restore` passed all 56 tests at the stage start. After import characterization and cancellation tests were added, the final run passed all 76 tests.

`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --no-restore` completed with 0 warnings and 0 errors.

`rtk proxy dotnet publish KoikatsuSceneGallery.csproj -c Release -r win-x64 --self-contained --no-restore -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -p:PublishDir=publish\smoke\` completed successfully. An earlier attempt failed because a previous smoke process (PID 48892) still locked the publish DLLs; the process was stopped and the identical publish command then passed.

The user manually exercised the unpackaged publish with compatible production plugins copied into the temporary publish folder: the window and Import page loaded, the BepisDB/WebView2 cookie setup dialog opened and closed normally, navigation returned to a 14,500-card Gallery with thumbnails intact, and no new crash-log entry appeared.

`rtk git diff --check` completed with no whitespace errors.

## Tests: 76 total / 76 passed / 0 failed / 0 skipped

## Changes

Added Core `ImportDestinationPolicy` and `ImportDuplicateDetector` characterization coverage for destination naming, artwork threshold behavior, duplicate filenames, file conflicts, card-type routing, synthetic-card equality, and cancellation. Extracted bounded file moves/duplicate deletion/collision handling into `ImportFileExecutor` with a maximum concurrency of four. Library filename/folder indexing now runs off the UI thread and observes cancellation. Extracted manual-assignment snapshots and undo state from `ImportViewModel` into `ImportManualAssignmentHistory`. `ImportService` decreased from 770 to 694 lines and `ImportViewModel` from 1,347 to 1,279 lines without schema or plugin-contract changes.

## Notes / out-of-scope findings

No new out-of-scope findings were added. Production plugin DLLs were copied only to ignored `publish\smoke\Plugins` for runtime validation and are not part of the commit.

## Risks

Independent file moves can now finish in a different order because they run with bounded parallelism; per-item status and duplicate/collision semantics remain unchanged. Cancellation is checked during library scans, file comparison, moves, and empty-directory cleanup; already-completed moves are not rolled back, matching the prior non-transactional behavior.

## Task 5 — Gallery/scan cancellation and bounded concurrency

## Result: Completed

## Commands run

The stage-start `rtk test dotnet test --no-restore` passed all 76 tests. After the Task 5 cancellation/concurrency coverage was added, the final run passed all 79 tests.

`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --no-restore` completed with 0 warnings and 0 errors after the final changes.

`rtk proxy dotnet publish KoikatsuSceneGallery.csproj -c Release -r win-x64 --self-contained --no-restore -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -p:PublishDir=publish\smoke\` completed successfully in the required unpackaged distribution mode.

The published `KoikatsuSceneGallery.exe` loaded the existing 14,500-card Scene Gallery and displayed thumbnails. UI automation rapidly switched six times across Scene, Character, and Coordinate galleries; each page remained responsive and loaded its cards. After leaving the active Gallery pipeline for Settings, two five-second process samples confirmed the process remained responsive and CPU fell from approximately one fully used core during active metadata parsing to 14.06% of one core. The smoke process was then closed normally.

`rtk git diff --check` completed with no whitespace errors.

## Tests: 79 total / 79 passed / 0 failed / 0 skipped

## Changes

Added cancellation tokens to card scanning, thumbnail generation/cache clearing, and metadata parsing APIs and threaded them through all Gallery call sites. Gallery pages now activate work on navigation, cancel load/thumbnail/metadata pipelines on departure, and defer folder-change reloads received while inactive. Main Scene cache hydration yields the UI thread in 200-card batches, while the other galleries retain bounded dispatcher batches with cancellation-safe backpressure.

Added the Core `BoundedAsyncPipeline` and used it for metadata work with a maximum concurrency of four, replacing one-`Task.Run`-per-card fan-out. Thumbnail work remains viewport driven behind a CPU-based semaphore, live file-watcher additions use the same bounded thumbnail path, and scan parallelism is bounded to the processor count. Detail metadata loads now cancel when the selected card or page changes.

Added three Core tests proving configured concurrency is respected, cancellation prevents new work from starting, and non-cancellation processor exceptions propagate.

## Notes / out-of-scope findings

No new out-of-scope findings were added. Existing user-owned changes to `AGENTS.md`, `docs/REFACTOR_PLAN_BACKUP.md`, and the untracked text file were preserved and excluded from the Task 5 commit.

## Risks

Windows imaging APIs do not accept `CancellationToken`, so in-flight WinRT decode/encode calls are cooperatively cancelled at every available await boundary rather than interrupted inside the native operation. Already completed thumbnails and metadata cache entries remain valid. Active metadata parsing can intentionally consume CPU while its Gallery is visible; the runtime smoke verified that usage falls after navigation cancellation.

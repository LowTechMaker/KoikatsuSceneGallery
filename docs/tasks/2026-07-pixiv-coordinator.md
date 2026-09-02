# Pixiv fetch coordinator extraction

## Outcome

`PixivAuthorPlugin` is now a 172-line SDK façade instead of a 347-line entry type. It retains all five SDK capability interfaces plus `IDisposable`, public signatures, settings and secret exchange, parser and URL entry points, SauceNao integration, host wiring, and disposal ownership. Author and artwork fetch coordination moved into one 215-line internal `PixivFetchCoordinator`; no second coordinator or runtime assembly was introduced.

The extraction is a behavior-preserving source move. Existing test assertions and test source files were not edited. SauceNao, both disk-cache classes, `PixivApiClient`, Plugin SDK/Common packages, application code, and other plugin repositories were not changed.

## Ownership

| Member group | Final owner | Reason |
| --- | --- | --- |
| `Name`, `Version`, `Settings`, `ProviderId` | `PixivAuthorPlugin` | SDK capability and plugin metadata surface |
| `Initialize`, `InitializeForTests`, `_host`, timing constants | `PixivAuthorPlugin` | Host and dependency wiring; the internal test signature is unchanged |
| `DestinationFolderName`, `UsesRatingFolders`, `GetSettingValue`, `SetSettingValue`, `_settings` | `PixivAuthorPlugin` | Settings and plaintext secret exchange with the host |
| Folder, filename, URL parsers and `GetArtworkUrl` | `PixivAuthorPlugin` | Thin capability entry points over existing parsers |
| Public `GetProfileUrl` | `PixivAuthorPlugin` façade, delegating to the coordinator's internal static formatter | Public SDK surface remains stable while the URL rule has one source |
| `SearchImageAsync`, `_sauceNaoClient` | `PixivAuthorPlugin` | SauceNao remains a separate existing client and was not wrapped |
| Public `GetAuthorInfoAsync`, `FetchArtworkInfoAsync` | `PixivAuthorPlugin` façade, delegating to `PixivFetchCoordinator` | Public signatures and pre-initialization null behavior remain stable |
| API client, both caches, avatar directory, author/artwork in-flight dictionaries, unsaved-artwork dictionary | `PixivFetchCoordinator` | Shared fetch state and lifecycle |
| `FetchAndCacheAsync`, `FetchArtworkAsync`, `ToAuthorInfo`, `ToArtworkInfo`, default-avatar filtering | `PixivFetchCoordinator` | Cache records, API results, failure landing, and SDK result conversion form one fetch workflow |
| Public `Dispose` | `PixivAuthorPlugin`, delegating first to the coordinator and then SauceNao | Preserves cache, API client, then SauceNao disposal order |

## Dependencies

```text
PixivAuthorPlugin façade
├─ PixivFetchCoordinator
│  ├─ PixivApiClient → RateLimiter
│  ├─ AuthorDiskCache → DebouncedDiskPersistence / AtomicFileWriter
│  ├─ ArtworkDiskCache → DebouncedDiskPersistence / AtomicFileWriter
│  └─ log callback / avatar directory
├─ PluginSettings → DpapiSecretProtector
├─ SauceNaoClient → separate RateLimiter
└─ existing folder and filename parsers
```

The coordinator has no dependency on the façade, `PluginSettings`, DPAPI, or SauceNao. The façade is the only construction root, so the graph has no cycle. The two production RateLimiter instances remain separate exactly as before.

## Test construction and coverage

`InitializeForTests(IPluginHost, PixivApiClient)` retains its name, signature, and façade entry point. It now constructs the coordinator with the injected client after recording the host. `PixivAuthorNegativeCacheTests.PluginTestContext` therefore still creates `PixivAuthorPlugin`, calls the same initializer, and exercises fetch behavior through the public capability methods; no assertion or construction code changed.

| Existing test layer | Cases | Result after extraction |
| --- | ---: | --- |
| Facade-through author/artwork cache, classification, and shared cancellation tests | 11 | 11 passed |
| Direct `PixivApiClient` parsing test | 1 | 1 passed |
| Direct author/artwork disk-persistence tests | 8 | 8 passed |
| Direct DPAPI helper tests | 6 | 6 passed |
| Direct settings secret-storage tests | 3 | 3 passed |
| Total | 29 | 29 passed, 0 failed, 0 skipped |

## Behavior-preservation source index

The pre-move references below use `HEAD:PixivAuthorPlugin.cs`; the post-move references use `PixivFetchCoordinator.cs`. Null-forgiving operators disappeared only because coordinator dependencies are constructor-required. `_host?.Log(...)` became the injected non-null `_log(...)` callback, and the artwork provider comparison now reads the same `PixivFolderNameParser.ProviderId` constant directly. The remaining statements and their ordering are retained.

| Behavior | Before | After | Evidence |
| --- | --- | --- | --- |
| Profile URL single source | façade lines 79 | coordinator lines 31–32; façade line 67 delegates | Same URL interpolation and public signature |
| Full author cache/fetch/conversion path | façade lines 81–151 | coordinator lines 34–107 | Cache lookup, force refresh, classified result landing, avatar handling, mapping, and final removal remain together |
| Force refresh and shared author cancellation | façade lines 89–95 | coordinator lines 45–51 | `TryRemove`, `CancellationToken.None`, and caller `WaitAsync(ct)` retain their order |
| Author failure landing and cache writes | façade lines 102–139 | coordinator lines 56–95 | Only `NotFound` writes `Failed: true`; `SchemaError` and exceptions return without a cache write; success performs one `Set` |
| Full artwork cache/fetch/conversion path | façade lines 240–339 | coordinator lines 109–208 | Cache lookup, promotion, de-duplication, fetch, conversion, and final removal moved as one block |
| Title-null refetch, unsaved promotion, shared cancellation | façade lines 248–262 | coordinator lines 117–131 | A cached null title still falls through; promotion performs one `Set`; shared fetch still uses `CancellationToken.None` and caller `WaitAsync(ct)` |
| Artwork null and fresh-result write policy | façade lines 272–292 | coordinator lines 141–161 | `data is null` returns before record creation or `Set`; saved success performs one `Set`; unsaved success only updates `_unsavedArtworkDetails` |

The artwork cache still contains its historical failed-entry schema and seven-day TTL, but the production fetch path still never creates a failed artwork entry. A null API result is therefore not negative-cached. The author path still negative-caches only classified `NotFound` for 24 hours; schema and transient failures remain retryable.

The disk-cache classes were untouched. Their generation counter increments only through the same four logical `Set` sites as before: author not-found, author success, saved artwork success, and unsaved-artwork promotion. The save-false artwork path still updates only the in-memory unsaved dictionary. This preserves generation, dirty flag, debounce, retry, and flush semantics.

## Verification

The Pixiv suite passed 29/29 with unchanged test sources. A Release `win-x64` build of `SceneGallery.Plugin.PixivAuthors` completed with zero warnings and zero errors. Main application tests passed 79/79 and plugin-load smoke tests passed 5/5.

All plugin, application, test, and smoke assets were restored for `win-x64` before integration verification. The sandbox could not read the normal user NuGet configuration, so restore used the existing workspace-local NuGet profile. The local/offline feed was sufficient for plugin packages; the application restore used the repository-owned package-validation configuration with approved nuget.org access for current MessagePack, WinUI, WebView2, SDK BuildTools, and Crossgen2 packages.

PowerShell 7 `Publish-WithPlugins.ps1 -NoRestore` completed into `artifacts/pixiv-coordinator/win-x64`. The output contains all four plugin directories and `plugins.manifest.json`. `Plugins/PixivAuthors` contains one `SceneGallery.Plugin.PixivAuthors.dll`, and no `SceneGallery.PluginSdk.dll` exists anywhere below `Plugins/`.

## Files changed

| Repository | File | Change |
| --- | --- | --- |
| PixivAuthorsPlugin | `PixivAuthorPlugin.cs` | Reduced to the SDK/settings/SauceNao façade and coordinator delegation |
| PixivAuthorsPlugin | `PixivFetchCoordinator.cs` | Added internal author/artwork fetch coordinator |
| KoikatsuSceneGallery | `docs/plugin-conventions.md` | Added scale-sensitive façade/coordinator granularity guidance |
| KoikatsuSceneGallery | `docs/tasks/2026-07-pixiv-coordinator.md` | Added this closing handoff |

No commit or push was performed. Existing user-owned `.claude/` directories and unrelated dirty application files were not modified.

## Cleanup-plan handoff index

| Record | One-sentence guide |
| --- | --- |
| `2026-07-plugin-packaging.md` | Established the five-repository publication script, manifest, isolated plugin directories, and loader smoke suite. |
| `2026-07-release-versioning.md` | Made release tags the SemVer source of truth and constrained updater asset selection to exact plugin DLL names. |
| `2026-07-pixiv-cache-cancellation.md` | Added Pixiv failure classification, 24-hour confirmed-not-found caching, caller-local shared cancellation, and the first plugin test gate. |
| `2026-07-cache-dirty-flag-tests.md` | Standardized generation-guarded debounced persistence and added cache behavior tests across Pixiv, Bepis, and Fanbox. |
| `2026-07-secret-storage.md` | Protected Pixiv SauceNao and Bepis cookie secrets at rest with versioned DPAPI migration semantics. |
| `2026-07-plugin-packages-gate1.md` | Created and locally validated the binary SDK and source-only Common package families. |
| `2026-07-plugin-packages-gate2.md` | Switched all plugins to the exact SDK package while keeping the host as the sole runtime contract assembly source. |
| `2026-07-plugin-packages-gate3.md` | Adopted source-only RateLimiter, persistence, and DPAPI infrastructure without changing provider policy or release shape. |
| `2026-07-remote-publication.md` | Completed GitHub Packages access, clean remote plugin CI, main-repository merge verification, and an end-to-end versioned release. |
| `2026-07-bepisdb-cleanup.md` | Removed unreachable Bepis WebView2 code and hardened response ownership, shared cancellation, and HTTP failure classification. |
| `2026-07-fanbox-lifecycle-fixes.md` | Fixed WebView2 cancellation, navigation bounds, initialization rollback, and bounded operation drain behavior. |
| `2026-07-fanbox-split.md` | Split the large Fanbox client by browser lifecycle responsibility and added plugin-level shared-deadline/deferred-cache disposal. |

This coordinator extraction closes the planned plugin cleanup: packaging and versioning, failure semantics, cancellation, persistence, secret protection, package provenance, remote verification, Bepis cleanup, Fanbox lifecycle and decomposition, and the final Pixiv façade boundary are all documented and verified.

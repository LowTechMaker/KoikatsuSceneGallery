# Plugin packages and shared infrastructure — Gate 3

## Status

Gate 3 is complete locally and stopped at the requested review gate. RateLimiter, debounced disk persistence, and DPAPI secret protection were adopted in that order, with an independent full verification after each step. No previously unknown behavior difference was found, no Plugin SDK public signature changed, no cache schema, TTL, key, comparer, or release shape changed, and no commit or push was performed.

The Common and Secrets source files prepared in Gate 1 already matched the live implementations, so no package-source implementation required modification in this gate. The work is entirely in package consumption, plugin wrappers, removal of duplicate local sources, conventions, and this handoff.

## Gate 3.1 — RateLimiter

PixivAuthorsPlugin, BepisDbPlugin, and FanboxWebView2Plugin now reference `SceneGallery.PluginCommon` `0.1.0` with `PrivateAssets="all"`. Their local `RateLimiter.cs` files were removed. Plugin and test projects use the `SceneGallery.PluginCommon` namespace through MSBuild `Using` items; no test body or assertion changed.

Before extraction, all four implementations were compared. Semaphore ownership, caller cancellation, jitter calculation, elapsed-time measurement, release-time timestamping, and idempotent releaser disposal were equivalent. Differences were limited to namespace, comments, blank lines, and effective member visibility inside an internal class.

The package validation build succeeded with zero warnings and no Common runtime assembly. Fresh isolated restores below `artifacts/gate3-rate-limiter` recorded `SceneGallery.PluginCommon/0.1.0` and the package RateLimiter content file, with no local RateLimiter source. Pixiv passed 29/29, Bepis passed 58/58, and Fanbox passed 27/27. Main application tests passed 79/79, plugin smoke tests passed 5/5, and `Publish-WithPlugins.ps1 -NoRestore` completed with all four plugins.

## Gate 3.2 — Debounced persistence

Pixiv `AuthorDiskCache` and `ArtworkDiskCache`, Bepis `ArtworkDiskCache`, and `FanboxMetadataCache` now delegate persistence mechanics to `DebouncedDiskPersistence` and production atomic writes to `AtomicFileWriter`. Their dictionaries, records, load paths, JSON schemas, TTLs, lookups, key generation, and log prefixes remain in their repositories.

The wrappers still expose the existing internal `IsDirty`, `RetryDelay`, and `Flush` seams, so every existing test ran without modification. The injectable atomic-writer constructor signature also remains unchanged. Fanbox still updates the requested stable ID, canonical creator/post ID, and bare post ID before making exactly one `MarkDirty()` call. `Clear` remains one dictionary clear followed by one `MarkDirty()`.

Before extraction, all four implementations were compared against Common. The two-second debounce, generation increment point, save lock, live dictionary serialization, generation-guarded dirty clear, five-second exponential retry capped at five minutes, success reset, temp-file overwrite, and Dispose flush semantics were equivalent. No provider-specific behavior entered Common.

The package validation build again succeeded with zero warnings. Fresh isolated restores below `artifacts/gate3-persistence` were followed by the unchanged cache tests and full suites: Pixiv 29/29, Bepis 58/58, Fanbox 27/27, main application 79/79, and smoke 5/5. Full publication completed successfully.

## Gate 3.3 — DPAPI secrets

PixivAuthorsPlugin and BepisDbPlugin now reference `SceneGallery.PluginCommon.Secrets` `0.1.0` with `PrivateAssets="all"`. Their local `DpapiSecretProtector.cs` files and direct `System.Security.Cryptography.ProtectedData` PackageReferences were removed. Fanbox and GitHubReleaseUpdate do not reference the Secrets package.

The two local implementations and the package candidate were compared before extraction. The `dpapi:v1:` prefix, UTF-8 conversion, DPAPI `CurrentUser` scope, null and empty handling, legacy migration signal, reserved-prefix behavior, invalid-base64 handling, cryptographic-failure handling, and fixed safe warning text were equivalent. The only differences were XML documentation and effective member visibility inside an internal type. Both DPAPI test files retained every original assertion.

The final Pixiv and Bepis assets graphs contain the Secrets Dpapi content file and exact `System.Security.Cryptography.ProtectedData/10.0.9` dependency. Their plugin outputs contain `System.Security.Cryptography.ProtectedData.dll`, contain no Common or Secrets runtime DLL, and have no local Dpapi source. Fanbox's assets graph contains no Secrets reference.

The normal elevated validation request could not run because the workspace approval-credit pool was exhausted. A workspace-local NuGet profile avoided reading the inaccessible user configuration, but its first fresh restore was then blocked from nuget.org by sandbox network policy. Package creation itself succeeded. Validation continued through a strictly offline equivalent: previously verified `.nupkg` files from the Gate 3 isolated caches were copied into ignored `artifacts/offline-nuget-feed`, the newly packed SceneGallery packages were added, and a temporary ignored source-mapping config restored into new empty Gate 3.3 caches without external settings or network access.

That offline package validation built with zero warnings. Its assets contained three Common content files, one Secrets Dpapi content file, the exact ProtectedData dependency, and no Common runtime DLL. Fresh Pixiv and Bepis restores below `artifacts/gate3-secrets` succeeded; Pixiv passed 29/29 and Bepis passed 58/58. Main application tests passed 79/79, smoke tests passed 5/5, and full publication completed successfully.

## Preserved boundaries

All Common and Secrets supplied types remain internal. Plugin SDK signatures and `PluginLoadContext` are unchanged. Cache dictionaries, entry schemas, TTLs, key strategies, comparers, load behavior, and mutation policy remain plugin-owned. Fanbox remains unsplit and still performs its three-key mutation before one dirty mark. Individual plugin release workflows still publish one main plugin DLL.

The existing test source files were not edited. Only the three affected test project files received a global Common namespace import so their InternalsVisibleTo access resolves the package-compiled internal types. GitHubReleaseUpdate received no Gate 3 source or project change beyond its already completed Gate 2 package switch.

## Remaining external verification

As in Gate 2, no remote GitHub Packages publication or restore was performed because commit, push, and tag creation remain forbidden. Package visibility, Actions access, real `GITHUB_TOKEN` authentication, and clean remote CI still belong to the documented manual publication sequence. The required push order remains application/package workflow first, package publication and repository access second, then the four plugin repositories.

Existing user-owned `.claude/` directories and unrelated main-repository changes were not modified. Gate 4 or the deferred Fanbox/Pixiv structural refactors have not started.

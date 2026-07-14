# Plugin engineering conventions

## External API failure classification

Rule: Classify failures as `NotFound`, `SchemaError`, or `TransientError`; only confirmed `NotFound` may enter a short-TTL negative cache.

Why: A Pixiv schema drift was previously treated as a missing author and hid valid data for seven days.

## Shared fetch cancellation

Rule: A de-duplicated shared fetch must not use any caller's `CancellationToken`; each caller waits with `WaitAsync(token)` or an equivalent caller-local mechanism.

Why: The first Pixiv caller could cancel the shared task and force unrelated waiters to receive null.

## Injectable HTTP transport

Rule: HTTP clients must accept an injectable `HttpMessageHandler` through an internal constructor exposed to tests with `InternalsVisibleTo`; network tests must run entirely offline.

Why: The Pixiv client originally constructed its transport internally, preventing deterministic tests for malformed, failed, and delayed responses.

## Test and CI gate

Rule: Before fixing a bug or adding a feature, the repository must have a test project and CI must execute it with `dotnet test`.

Why: The Pixiv cache and cancellation fixes needed a repeatable safety net before production behavior could be changed.

## Debounced disk-cache persistence

Rule: A cache mutation increments a generation counter, marks the cache dirty, and schedules a debounced flush. A flush holds the save lock, snapshots the generation, writes atomically through an injectable writer, and clears dirty only when the generation is unchanged. Failed writes keep dirty set and retry from five seconds with exponential backoff capped at five minutes; any successful write resets the backoff.

Why: Clearing dirty before a write lost changes on I/O failure, while clearing it after an unguarded live-dictionary serialization could hide a mutation made during the write.

## Secret settings at rest

Rule: Plugin-owned secret settings must be encrypted at rest with Windows DPAPI `CurrentUser` scope and a versioned value prefix. Keep plaintext only in memory and across SDK capability calls. Legacy plaintext may remain readable but must be encrypted at the next existing save point. If decryption fails, treat the value as unset and log only the field name and failure category, never plaintext, ciphertext, or fragments of either.

Why: The SDK `Secret` value type only selects a masked editor; it does not protect plugin settings on disk. Versioned DPAPI values allow smooth migration without changing the settings JSON schema, while safe failure handling covers another computer, another Windows user, and damaged data without leaking the secret into logs.

## Plugin SDK package consumption

Rule: `SceneGallery.PluginSdk` remains a contracts-only binary package with an exact SemVer reference. Plugin projects consume its compile asset with `ExcludeAssets="runtime"` and `PrivateAssets="all"`; the host application remains the only runtime source of `SceneGallery.PluginSdk.dll`. Keep `AssemblyVersion` fixed for the lifetime of one major package version while file and informational versions follow the package version.

Why: A copied SDK DLL creates a second contract type identity inside the plugin load context, while a changing assembly version can prevent an otherwise compatible 1.x plugin from binding to the host contract.

## Source-only plugin infrastructure

Rule: Shared plugin implementation packages use `contentFiles/cs/any` with `buildAction="Compile"`, contain no runtime assembly, and declare every supplied type `internal`. Persistence infrastructure owns only debounce, generation, atomic-write, retry, and disposal mechanics; entry schemas, TTLs, key strategies, dictionary comparers, and logical mutations remain plugin-owned.

Why: Plugins are released as one main DLL. Compiling internal common sources into each plugin preserves that deployment shape, avoids public duplicate type names across plugin assemblies, and prevents a generic cache abstraction from erasing provider-specific behavior such as Fanbox's three-key mutation.

## Plugin package source provenance

Rule: Every plugin repository owns a `NuGet.config` that maps `SceneGallery.*` only to the `SceneGalleryGitHub` source. CI supplies that source's credentials through `NuGetPackageSourceCredentials_SceneGalleryGitHub` and `GITHUB_TOKEN`; credentials never enter a tracked file. Before a package exists remotely, local equivalence tests must explicitly use the application repository's `eng/PackageValidation/NuGet.config` and an isolated package cache against `artifacts/local-feed`. Do not add the local feed to the committed plugin configuration.

Why: A local fallback in the committed source list can hide a missing or inaccessible GitHub package, while relying on a populated global package cache can make a package-only build appear standalone when it is not. The explicit local config keeps pre-publication validation possible without weakening the production source mapping.

## Staged source-only extraction

Rule: Compare every plugin-local implementation before adopting a shared source-only package, then extract one primitive per gate. Keep provider policy in the plugin wrapper, reference the exact source package version with `PrivateAssets="all"`, and remove the local source only after a fresh isolated restore proves the package content file was compiled. Existing behavior tests keep their assertions; only namespace, construction, or injection plumbing may change.

Why: Similar-looking copies can hide provider-specific semantics. One-primitive gates make an unexpected difference attributable and reversible, while isolated package restores prevent a sibling checkout, stale build output, or global package cache from falsely proving the extraction.

## Narrow callback seams for dependency cycles

Rule: Break a lower-layer-to-higher-layer dependency with the narrowest semantic callback rather than storing the higher-layer service. The callback contract must document what success means, who owns cancellation, and whether cancellation must propagate.

Why: Fanbox challenge navigation needed to probe API usability, but giving Navigator an ApiClient reference would create a cycle. A documented `Func<CancellationToken, Task<bool>>` kept Navigator dependent only on Host while preserving the linked caller/challenge token semantics.

## Evidence for runtime-only behavior moves

Rule: When extracting behavior that cannot be deterministically exercised offline, preserve the critical block statement-for-statement and record a before/after source index in addition to the available regression tests. Explicitly identify which behavior remains manually verified; source comparison is evidence for a move, not a substitute for a feasible test seam.

Why: WebView2 does not expose cancellation for an executing script. Comparing the moved `AsTask`/`WaitAsync`/abandoned-observer block proved the lifecycle fix stayed intact without pretending an offline parser suite exercised a real browser operation.

## Shared disposal deadlines and deferred persistence

Rule: The outer resource owner marks itself disposing before waiting, rejects new producers, and measures one monotonic disposal deadline. Nested owners receive only the remaining time and must not restart the timeout. Dispose execution resources before persistence; if producers outlive the deadline, force execution teardown and defer cache flush/disposal until the last producer finishes. Run dependency disposal outside the operation-count lock.

Why: Independent plugin and WebView drains could otherwise wait ten seconds each, while disposing Fanbox cache before an end-to-end producer completed allowed a later cache mutation or user-input retry to touch disposed persistence.

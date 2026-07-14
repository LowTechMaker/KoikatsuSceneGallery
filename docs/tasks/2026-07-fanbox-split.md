# Fanbox WebView2 client split

## Outcome

The original 1,082-line `FanboxWebView2Client` was split behind the same internal
façade name without changing its public constructor or fetch/disposal signatures.
The plugin still publishes one main `SceneGallery.Plugin.Fanbox.dll`; the split adds
source types, not runtime assemblies. Existing test assertions were retained.

Gate 4 also moves end-to-end artwork producer lifetime ownership into
`FanboxPlugin`. Plugin disposal now stops new producers, shares one ten-second
deadline with the WebView host, disposes the host before the cache, and defers cache
flush/disposal until the last producer finishes if the deadline expires.

## Responsibilities and dependencies

`FanboxWebView2Client` remains the compatibility façade. It constructs the host and
API client, delegates the existing fetch methods, and preserves the original name
and signatures.

`FanboxWebViewHost` exclusively owns the DispatcherQueue thread, WebView2
environment/controller, hidden Win32 window, initialization rollback, idle shutdown,
host operation counter, bounded disposal, reverse-order teardown, and script-result
unwrapping.

`FanboxNavigator` owns ordinary navigation, API navigation, post-page script
capture, age-gate interaction, challenge-window polling, and the single sources for
the 40-second navigation timeout and five-minute challenge timeout. Its challenge
loop accepts only a documented `Func<CancellationToken, Task<bool>>` API probe and
does not reference `FanboxApiClient`.

`FanboxPageParser` has no browser dependency. It deserializes synthetic post-page
snapshots and applies hydration, meta, URL, tag, rating, and missing-page fallback
rules.

`FanboxApiClient` owns retries, rate limiting, creator/post orchestration, API result
classification, page parsing, and caller-local script waiting. It depends on the
host, navigator, parser, and rate limiter. The abandoned-script continuation only
observes and logs later faults.

The effective dependency graph is:

```text
FanboxPlugin
  -> FanboxWebView2Client façade
       -> FanboxWebViewHost
       -> FanboxApiClient
            -> FanboxWebViewHost
            -> FanboxNavigator -> FanboxWebViewHost
            -> FanboxPageParser
            -> RateLimiter

FanboxPageParser -> no browser or plugin dependency
```

## Plugin-level drain and deadline semantics

The de-duplicated artwork `Lazy<Task<ArtworkInfo?>>` factory is the producer
boundary. `FanboxPluginOperationDrain.RunProducerAsync` increments immediately
outside the full producer and decrements from one `finally` after user input,
WebView calls, result conversion, cache mutation, and in-flight removal have ended.
Each caller still waits on the shared task through `WaitAsync(callerToken)`, so a
caller leaving does not decrement the producer count or cancel shared work.

Disposal uses a monotonic stopwatch and one ten-second total deadline. Under the
operation lock it marks the plugin disposing, rejects new producers, then waits with
`Monitor.Wait`; the wait releases the lock so producer `finally` blocks can decrement
and pulse. Dependency disposal runs outside that lock. The client receives only the
remaining duration through an internal overload, and the host performs no further
wait when the remaining duration is zero. This prevents nested ten-second waits.

The host is always disposed before the cache. When producers drain in time, cache
`Dispose` runs after host disposal and synchronously flushes dirty persistence. When
the plugin deadline expires, host teardown is forced with zero remaining time and
cache disposal is marked deferred. The last producer disposes the cache from its
`finally`, but only after host disposal has completed. Coordination flags make cache
disposal exactly once even when producer completion races host teardown.

Client creation is serialized with host disposal. A client created before disposal
is observed and disposed; creation after host disposal begins is rejected. This
closes the race where a producer admitted near shutdown could otherwise publish a
new client after the plugin had already disposed its dependencies.

## Parser coverage and JavaScript priority rules

Gate 1 added 32 offline parser cases using synthetic snapshot and hydration JSON;
they do not initialize WebView2. Coverage includes malformed and non-target
hydration data, nested fallback fields, leading-zero post IDs, HTML cleanup, string
and object tags, adult markers, 404 URLs, missing creators, and missing metadata.

| Value | Priority preserved from page JavaScript and parser fallback |
| --- | --- |
| Title | hydration title, Open Graph title, Twitter title, document title, post ID |
| Description | hydration description, Open Graph description, meta description |
| Creator ID | hydration creator, canonical URL, Open Graph URL |
| Author name | hydration user/fallback name, author parsed from title, creator ID |
| Tags | non-empty hydration tags, filtered case-insensitive distinct keywords |
| Adult rating | hydration flag OR body/keyword `R-18`, `R-18G`, or adult marker |

JavaScript `||` treats an empty string as falsy but a whitespace-only string as
truthy. The snapshot keeps the source fields separate so C# can reproduce that
selection before cleanup. Consequently whitespace Open Graph title prevents lower
title sources and later falls back to the post ID, while whitespace Open Graph
description suppresses the lower meta description and cleans to null. Dedicated
characterization tests lock both cases.

## Gate evidence index

| Gate | Evidence |
| --- | --- |
| Gate 0 | Ownership and dependency direction were fixed before edits; the API probe callback broke the Navigator/API cycle without a back-reference. |
| Gate 1 | Commit `1355c0c` extracted `FanboxPageParser`; 32 offline cases locked the JavaScript/C# priority rules. |
| Gate 2 | Commit `87d3153` extracted `FanboxWebViewHost`; initialization rollback, idle shutdown, operation accounting, ten-second drain, reverse teardown, and `UnwrapScriptResult` moved together. |
| Gate 3 | Commit `17a4a52` extracted Navigator and API client. The pre-move `FetchApiAsync`/`ObserveAbandonedScript` block was compared statement-for-statement with the new block: `AsTask`, caller-local `WaitAsync(ct)`, filtered cancellation catch, observer registration, rethrow, and observer continuation options remained unchanged. Each API fetch still has one Host `BeginOperation` and one `finally` `EndOperation`; the observer has neither. |
| Gate 4 | `FanboxPluginOperationDrainTests` covers real cache flush after normal drain, public rejection after disposal, zero-remaining forced host disposal without deadlock, and exactly-once deferred cache disposal after the final producer. |

## `FanboxApiModels.ParsePost` observation

`FanboxApiModels.ParsePost` is currently referenced only by
`FanboxApiModelsTests`. Production creator API handling uses `ParseCreator`, while
production post handling uses `FanboxPageParser.ParsePost` on a captured page
snapshot. The test-only API parser was intentionally neither removed nor redirected
during this split; deciding whether it remains compatibility coverage or dead code
is a separate cleanup decision.

## Automated verification

All application, smoke-test, plugin, and Fanbox-test projects were restored for
`win-x64` with the repository-owned package-validation configuration and local feed.
The final gates passed with 64 Fanbox tests, 79 main-application tests, and five
plugin-load smoke tests. The Fanbox Release win-x64 build completed with zero
warnings and zero errors.

`Publish-WithPlugins.ps1 -NoRestore` completed successfully into the isolated
`artifacts/fanbox-gate4/win-x64` directory. The output contains all four plugin
directories and one Fanbox main DLL, while no `SceneGallery.PluginSdk.dll` appears
below `Plugins/`. The pre-existing dirty files and plugin `.claude/` directories
were not modified.

## Manual WebView2 verification checklist

These application-level checks remain manual because they require a real WebView2
profile, FANBOX pages, and interactive application shutdown. Use the same known
scene and cache-clearing setup recorded in `2026-07-fanbox-lifecycle-fixes.md`.

- [ ] Normal fetch: clear Fanbox metadata cache, import a known scene, and confirm creator/title metadata completes normally.

- [ ] Cancellation followed by another fetch: cancel the first caller while it displays `Analyzing`, start the same request again, and confirm the shared producer completes for the later caller.

- [ ] Close during fetch: close the application while a real fetch is active and confirm the window exits without a hang or lifecycle exception.

- [ ] Drain observation during close: while a fetch is active, close the application and confirm plugin drain finishes or forces teardown within the shared ten-second deadline; verify logs contain no cache-after-dispose exception, no second disposal, and no write attempted after deferred cache disposal.

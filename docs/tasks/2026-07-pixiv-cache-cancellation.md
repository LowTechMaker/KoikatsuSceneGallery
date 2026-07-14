# Pixiv cache classification and shared cancellation

## Outcome

The Pixiv plugin now has an xUnit test project and a CI `dotnet test` gate. Its author fetch path distinguishes confirmed missing resources from schema drift and transport failures, and its author/artwork de-duplication no longer allows one caller's cancellation token to cancel work shared by other callers.

## Failure classification and cache policy

`PixivApiClient.FetchUserAsync` returns an internal classified result. HTTP 404 is `NotFound`. An `error=true` response is also `NotFound` only when its message explicitly says that the resource was not found or does not exist, including the supported Japanese and Chinese equivalents. Confirmed `NotFound` results are negative-cached for 24 hours instead of the previous seven days.

A successful HTTP response with no object `body`, no usable `name`, or an ambiguous `error=true` is `SchemaError`. It returns null without writing any cache entry and logs a warning containing the reason and at most 500 characters of the raw response. JSON parsing exceptions still follow the pre-existing outer exception path and do not write a cache entry.

Network exceptions and exhausted HTTP 5xx retries remain transient failures. They follow the existing fetch-failure logging path, return null, and do not write a cache entry. Tests prove that the next call after schema or transient failure issues a new HTTP request, while confirmed not-found results suppress the second request until the 24-hour TTL expires.

## Shared cancellation design

The author and artwork in-flight dictionaries still de-duplicate concurrent requests by resource key. The shared fetch is now created with `CancellationToken.None`; each caller awaits the shared task with `WaitAsync(callerToken)`. This was selected over a reference-counted CTS because the shared HTTP/cache operation has no resource pressure that requires cancellation when an individual waiter leaves, and keeping it alive preserves useful work for remaining callers and the disk cache.

The cancellation tests start two callers on one controlled HTTP response. Caller A is cancelled and receives `OperationCanceledException`, caller B still receives the parsed result, and a third call reads the completed cache entry without another HTTP request. Separate tests cover both author and artwork pipelines.

## Test and CI coverage

`SceneGallery.Plugin.PixivAuthors.Tests` uses the same xUnit and test SDK versions as the main application tests. `PixivApiClient` keeps its production constructor and adds an internal `HttpMessageHandler` constructor exposed through `InternalsVisibleTo`. All HTTP responses, exceptions, retry sequences, and delayed shared responses are generated in memory; the test suite performs no network access.

The 12 tests cover the existing successful user response, HTTP 404, an explicit API not-found response, 23/25-hour negative-cache TTL boundaries, missing body, missing name, ambiguous `error=true`, a network exception, exhausted 503 retries, author shared cancellation, and artwork shared cancellation. The Pixiv workflow now runs the test project after its Release build.

Final local verification completed with the following results:

```text
Pixiv plugin tests:       12 passed, 0 failed, 0 skipped
Main application tests:  79 passed, 0 failed, 0 skipped
Plugin load smoke tests:   5 passed, 0 failed, 0 skipped
Full plugin publication:  succeeded with four plugin directories and plugins.manifest.json
```

## Problems encountered and decisions

Adding the test project below the plugin repository initially caused the plugin SDK's default compile glob to include test source files. The plugin project now excludes `SceneGallery.Plugin.PixivAuthors.Tests/**`; this keeps the test project physically close without compiling xUnit code into the plugin.

The first final smoke-test run failed with `NETSDK1047` because running the non-RID Pixiv unit tests had replaced the plugin's `project.assets.json` with an asset graph lacking `win-x64`. The smoke CI already restores every plugin with `-r win-x64`; performing the same RID restore locally fixed the smoke test without a source change. When running both suites manually, restore the Pixiv plugin for `win-x64` before invoking the main repository's `--no-restore` smoke tests.

The API's `error=true` field is not sufficient proof that a user is absent. The implementation deliberately recognizes only explicit not-found wording; unknown messages remain schema errors so an unofficial API wording change cannot poison the cache.

## General lessons

The reusable rules distilled from this work live in [`../plugin-conventions.md`](../plugin-conventions.md). Future sessions should update that standing document when a new cross-plugin rule is discovered rather than copying the rules into task handoffs.

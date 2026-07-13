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

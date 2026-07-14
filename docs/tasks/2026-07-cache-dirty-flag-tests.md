# Plugin disk-cache dirty flag and test gates

## Outcome

The two Pixiv caches, the Bepis artwork cache, and the Fanbox metadata cache now share the same persistence semantics. Cache entry schemas, TTLs, dictionary comparers, file names, and key strategies remain unchanged. Bepis and Fanbox now have xUnit projects and CI test gates for their first pure-logic candidates as well as their disk caches.

No commit or push was performed.

## Cache differences retained

| Repository | Cache and file | Entry/key strategy | TTL and lookup behavior | Mutations |
| --- | --- | --- | --- | --- |
| PixivAuthorsPlugin | `AuthorDiskCache`, `authors.json` | Pixiv author id to `CachedAuthor` | Successful entries remain until refresh; failed entries expire after 24 hours | `Set` |
| PixivAuthorsPlugin | `ArtworkDiskCache`, `artworks.json` | Pixiv artwork id to `CachedArtwork`; tags retain translated names | Successful entries remain until refresh; failed entries expire after 7 days | `Set` |
| BepisDbPlugin | `ArtworkDiskCache`, `artworks.json` | Bepis composite artwork id to `CachedArtwork`; uploader lookup scans values | Successful entries remain until refresh; failed entries expire after 7 days | `Set` |
| FanboxWebView2Plugin | `FanboxMetadataCache`, `webview2-metadata-cache.json` | Case-insensitive string keys to `PostEntry`; one post is indexed by the requested stable id, canonical `creator:post`, and bare post id | No negative-entry TTL; supports creator lookup | `SetPost`, `Clear` |

The Fanbox three-key write is a required behavior, not accidental duplication. A future shared cache component must allow one logical mutation to update multiple keys before incrementing one generation. It must also preserve Fanbox's case-insensitive comparer and the other caches' distinct TTL and entry-schema policies.

## Generation-counter persistence semantics

Each mutation first changes the live `ConcurrentDictionary`, then calls `Interlocked.Increment` on the generation counter. Under the save lock it marks dirty and reschedules the existing two-second debounce timer.

`Flush` now enters the save lock before inspecting dirty, snapshots the current generation, serializes the live dictionary, and performs the temp-file plus overwrite move through an injected atomic writer. A successful write resets the retry delay. Dirty is cleared only when the generation still matches the snapshot. If a mutation changed the generation during serialization or writing, dirty remains set and another debounced flush is scheduled.

On an exception, dirty remains set. Retries start at five seconds, double after each consecutive failure, and stop growing at five minutes. The first subsequent successful write resets the delay to zero. Dirty checks and clears no longer occur outside the save lock.

Live `ConcurrentDictionary` serialization remains intentional. It may produce a weakly consistent snapshot, but any mutation during the write changes the generation, so the cache cannot incorrectly consider that snapshot final. No deep copy was added.

Production constructors still expose the same API. Internal constructors accept the atomic writer for deterministic tests, and `InternalsVisibleTo` exposes only the necessary internal seams.

## Tests and CI

Every cache has tests for a failed write retaining dirty, a later retry reaching disk and resetting backoff, and a successful write clearing dirty. Every cache also has a generation-race test where a mutation occurs inside a successful write; the first flush must remain dirty and the next flush must persist the newer value. Failure tests verify the five-second initial delay, doubling, and five-minute cap. Fanbox additionally verifies that `Clear` persists an empty cache and that `SetPost` persists all three keys.

Bepis coverage includes `BepisDbFilenameParser`, `BepisDbAuthorFolderNameParser`, `BepisDbCategoryHelper`, and `RateLimiter`. Fanbox coverage includes `FanboxFilenameRules`, `FanboxAuthorSettings`, `FanboxPostKey`, and `FanboxApiModels`. Fanbox hydration parsing remains excluded because it is embedded in the WebView2 client structure that this task was not allowed to reshape.

The new projects are `SceneGallery.Plugin.BepisDb.Tests` and `SceneGallery.Plugin.FanboxWebView2.Tests`. Their plugin projects exclude the test subdirectories through `DefaultItemExcludes`, grant `InternalsVisibleTo`, and reference the tests with `DeployPluginToApp=false`. Both build workflows now run `dotnet test -p:DeployPluginToApp=false` after the Release build. Pixiv retains its existing project and CI gate.

Final local results:

```text
PixivAuthors tests:             20 passed, 0 failed, 0 skipped
  pre-existing tests:          12 retained and passing
  cache persistence tests:      8 passed (4 per cache)
BepisDb tests:                  29 passed, 0 failed, 0 skipped
FanboxWebView2 tests:           25 passed, 0 failed, 0 skipped
Main application tests:        79 passed, 0 failed, 0 skipped
Plugin load smoke tests:        5 passed, 0 failed, 0 skipped
Pixiv Release build:            succeeded
Bepis Release build:            succeeded with the pre-existing WindowsBase warning
Fanbox Release build:           succeeded
Main x64 Release build:         succeeded
```

Before the smoke tests, the application, smoke project, and all four plugin projects were restored for `win-x64`. This avoids the known `NETSDK1047` failure after non-RID unit tests replace a plugin asset graph.

Bepis and Fanbox tests were also rerun with a deliberately missing `SceneGalleryAppDir`; both suites passed against their repository-local fallback SDK DLLs, matching the standalone CI checkout shape. Their plugin projects were restored for `win-x64` again afterward so the local asset graphs were not left in the non-RID test state.

## Deferred work

Fanbox hydration parser tests remain assigned to the Fanbox client split task because the parser is currently coupled to the protected WebView2 client structure. Bepis `CookieHttpFetcher` HTTP failure-classification tests remain assigned to the Bepis cleanup task because handler and delay injection are outside this cache/test-gate task.

The existing Bepis `WindowsBase` conflict warning from WebView2 WPF assets remains unchanged and does not fail the build or tests.

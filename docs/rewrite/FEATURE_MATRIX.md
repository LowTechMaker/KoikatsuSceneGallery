# Feature matrix

| Task 1 capability | Legacy behavior source | Core/test coverage | Status |
| --- | --- | --- | --- |
| PNG dimension reading | `PngHelper` | Synthetic IHDR, short input, non-PNG signature behavior, Unicode paths | Implemented |
| Appended PNG boundary detection | `PngEmbeddedData` | Valid/truncated/non-PNG streams, non-zero stream position | Implemented |
| Character filename timestamp parsing | `CharacterCardFilenameParser` | Valid embedded timestamp, invalid date, marker and case boundaries | Implemented |
| Pixiv/Bepis filename link parsing | `FilenameLinkParser` | Pixiv, all Bepis prefixes, overlapping IDs, null, Unicode and case behavior | Implemented |
| Path sanitization | `PathSanitizer` | Platform-invalid characters, Unicode, repeated separators, long and duplicate inputs | Implemented |
| Shuffle ordering | `ShuffleQueueComparer` | Mapped order, equal order, unknown and null items | Implemented |
| Scene metadata parsing/classification | `SceneMetadataParser`, `SceneClassifier` | KK/KKS/unknown, token read-boundary overlap, invalid and missing input | Implemented |
| Card type classification | `CardTypeClassifier` | KK/KKS character, coordinate and scene cards, plain PNG and missing input | Implemented |
| Character metadata parsing | `CharacterCardParser` | Synthetic MessagePack Parameter/KKEx blocks, KK/KKS, Madevil and invalid input | Implemented |
| Coordinate metadata parsing | `CoordinateCardParser` | Named/empty coordinate, substring marker behavior, invalid input | Implemented |
| Pure metadata/source models | metadata records, `CardSourceClassifier` | Unknown sentinels, full-name behavior, all source precedence branches | Implemented |
| Resolution parsing | `ResolutionOption` | Valid, whitespace, negative/zero, invalid separator and arity behavior | Implemented |
| WinUI-free Core target | project boundary | `net10.0`, no `Microsoft.UI` or Windows target framework | Verified by test and Release build |
| Linux Core CI | `.github/workflows/build.yml` | Dedicated Ubuntu `dotnet test` job | Implemented; CI not run locally |
| Windows app compatibility | WinUI app build | Release x64 app-project build | Verified: 0 warnings, 0 errors |

The status column is updated after implementation and local verification. The Ubuntu job itself remains pending until CI runs.

## Task 2

| Capability | Implementation | Verification | Status |
| --- | --- | --- | --- |
| Single composition entry | `AppServiceRegistry` exposed only as `App.Services` | `App.xaml.cs` public-static scan | Verified |
| ViewModel constructor injection | Settings, gallery, media, import, author, and detail dependencies injected | Zero `App.` references under `ViewModels/` | Verified |
| Service isolation | Author-source orchestration moved to `AuthorSourceCoordinator` | Zero `App.` references under `Services/` | Verified |
| XAML code-behind resolution | Pages, MainWindow, and controls resolve through the single registry entry | Release x64 compilation | Verified |
| Delayed optional services | Import and author-post services are registered only when providers exist | Preserved composition order and nullable lookups | Build verified; runtime pending |
| App launch and gallery load | Current build launched with package identity | `winapp` unavailable | Blocked |

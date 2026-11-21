# Card metadata

## Repeatable CSV compatibility cases (2026-09-13)

`CardMetadataCsvCompatibilityTests` adds 12 fixed size-boundary expectations from
the pinned FileSize source and two complete 14-column CSV expectations (`en-US`
and `de-DE`). These cover KiB truncation, unit choice before rounding, the maximum
long input, quoted decimal commas, field order and trailing line breaks. The
existing exporter passes without changes. All 504 Core/component tests pass
(`artifacts/test-results/core/alexc_DESKTOP-KI82NU6_2026-09-13_01_32_04_net10.0.trx`).
These offline cases run in the existing Core suite; no sample files or network
reference are needed. This establishes source-based projection compatibility,
not a full real-card CSV comparison against a running KKManager installation.


## Independent extended-data contract comparison (2026-09-13)

Read the pinned KKManager `PluginData.cs` contract: MessagePack array keys 0 and 1
contain an integer version and a string-keyed object dictionary. An ignored local
probe independently walks PNG/header records and deserializes KKEx using that
typed wire contract. It does not call the production reader to locate/reference
the bytes and does not compile or execute downloaded KKManager code.

The probe compares GUIDs, versions, dictionary keys and recursively all supported
values against the production metadata tree after JSON serialization/deserialization
with `CardMetadataExport.JsonOptions`. It compares float32 and float64 at their
own precision, exact integer values, strings, booleans, nulls, arrays, maps and
binary bytes. An unsupported reference value type fails explicitly.

Result: **7 passed, 0 mismatches, 8 excluded by the existing 64 MiB block limit**.
Two passing cards have no KKEx tail; the other five contain 73 plugin entries and
238 top-level plugin data fields, including nested values. The initial comparison
incorrectly widened float32 round-trip text to double; fixing the probe's precision
handling resolved the three apparent differences. No product change was needed.

Reproduce: `rtk proxy dotnet run --project artifacts/metadata-reference-probe/MetadataReferenceProbe.csproj -c Release -- sample artifacts/metadata-reference-probe/report.json`.
The report retains source SHA-256 and aggregate counts, not payload values. Sample
cards and probe artifacts remain ignored and local. This is independent contract
validation using the locally installed MessagePack library, **not execution of
KKManager or parity with its installed-mod interpretation/UI**. The eight oversized
blocks, unsupported types absent from this corpus, full KKManager CSV output and
manual UI checks remain unverified. Prior stage's 490 Core/102 integration passes
and Release build are unchanged historical evidence for this probe-only step.


## CSV coordinate-name regression (2026-09-13)

The pinned KKManager `CardWindow.cs` lines 862–895 export `card.Name ?? ""`
in CharacterName. Unlike character cards, KoiCoordCard has no filename fallback.
The shared reader correctly retained an empty coordinate name, but ToCsv applied
the character filename fallback to it. Coordinate CSV now preserves that empty
value; character fallback, JSON, UI labels and naming policies are unchanged.

Three synthetic regression cases cover empty, whitespace-only and populated
coordinate names through the production reader and exporter. Before the fix the
empty-name case failed (two others passed); after the fix all 490 Core/component
tests passed. TRX evidence is under `artifacts/test-results/coordinate-csv-before`
and `artifacts/test-results/core` (2026-09-13 01:21 and 01:22 respectively).

Source comparison confirms the same 14-column order, full source path in FileName,
UTF-16 encoding and trailing empty line. Size formatting and extended block
counts still require independent output comparison for the full real-card corpus.
Missing-mod columns intentionally remain empty: this application does not inspect
installed mods. Its CSV quoting escapes embedded quotes, unlike the reference's
direct interpolation. These are explicit boundaries, not byte-for-byte parity.


## Coordinate name reference check (2026-09-13)

Inspected KKManager commit `11b31fcb012c269d6d19c3cf0eef7aae0466ceaf`,
[`KoiCoordCard.ParseKoiClothes`](https://github.com/IllusionMods/KKManager/blob/11b31fcb012c269d6d19c3cf0eef7aae0466ceaf/src/KKManager.Core/Data/Cards/KK/KoiCoordCard.cs).
It reads the version, then the coordinate name directly from the binary header;
the constructor assigns that name without a fallback. The downloaded source was
read only, saved as a `.txt` reference and never compiled or executed.

An independent local BinaryReader probe walks PNG chunks to IEND and reads those
header fields without calling CardMetadataReader. All three coordinate samples
have version `0.0.0`; name lengths are 15 (female), 23 (male), and **0**
(`sunshine_female.png`). Thus the empty name is present in the serialized header
and agrees with the reference's name policy; no product fix is warranted here.
Evidence: `artifacts/metadata-probe/coordinate-headers.json` and the reproducible
`check-coordinate-header.ps1` beside it. No card contents were uploaded or changed.
This checks the name field against source, not KKManager execution or complete
metadata equivalence. CSV and extended-data output comparison remains pending.

## Local real-sample probe (2026-09-13)

The ignored local `sample` directory contains 15 PNG cards. A read-only probe using
the current Core CardMetadataReader read all 15 in summary and expanded modes:
11 Koikatu, one KoikatsuSunshine and three KoikatuClothes. Card type, name and plugin
GUID list agreed between modes. Eight cards reported partial metadata because their
KKEx blocks exceed the existing 64 MiB limit (BlockSizeLimit); this is not full
extended-data validation. One coordinate sample has an empty name; no substitute
name or format policy was introduced without an independent baseline.

The local report is `artifacts/metadata-probe/report.json`, with per-file SHA-256,
block sizes/status/diagnostics and basic-field presence. The probe and input cards
remain ignored local artifacts; they were not added to CI or copied into tracked
fixtures. This evidence is stronger than synthetic-only testing, but is still not
a comparison with KKManager's own output. That comparison and UI checks remain open.

Current Core tests: 487 passed on the September 13 worktree. The older counts below
are historical; Windows integration/build were not rerun for this documentation and
read-only probe step.

## Current validation status (2026-09-08)

The current local TRX results contain 329 passing Core/component cases and
40 passing Windows integration cases, with no skips. CardMetadataTests accounts
for 15 Core cases using synthetic card data. These verify the supported layouts,
queries and export projections, not equivalence against a real-card corpus in
KKManager. That comparison and the detail-panel UI checklist remain unexecuted.
See ACCEPTANCE.md for the evidence matrix and IMPORT-REFACTOR.md for later stages.

Since the initial extraction described below, metadata application callbacks also
check the scan lifetime when executed; metadata refresh timers live in
GalleryMetadataRefresh. Cache persistence, first-load synchronization and operation
draining are shared. App shutdown waits up to one common five-second deadline
before closing; timeout or process termination does not guarantee a final write.
Rejected dispatches and queued callback failures are reported through the shared
scan callback. A rejected completion cannot update UI counters; restart or explicit
count-reset cancellation remains necessary in that case.
The source-index and 203-test result below describe the original extraction,
not the current worktree's total or current ownership.

KK/Party/KKS character cards and Koikatsu coordinate cards share a read-only
metadata reader. The character and coordinate detail pages expose an expandable
metadata panel with JSON and KKManager-compatible CSV export. Gallery queries
include names, nicknames, UserID, DataID and plugin GUIDs; metadata filters are
shared with group and author-scoped browsing.

## Format reference

Reference snapshot: IllusionMods/KKManager commit
`11b31fcb012c269d6d19c3cf0eef7aae0466ceaf`.

- [Card layouts and fields](https://github.com/IllusionMods/KKManager/tree/11b31fcb012c269d6d19c3cf0eef7aae0466ceaf/src/KKManager.Core/Data/Cards)
- [CSV columns and values](https://github.com/IllusionMods/KKManager/blob/11b31fcb012c269d6d19c3cf0eef7aae0466ceaf/src/KKManager/Windows/Content/CardWindow.cs)
- [Displayed file sizes](https://github.com/IllusionMods/KKManager/blob/11b31fcb012c269d6d19c3cf0eef7aae0466ceaf/src/KKManager.Core/Util/FileSize.cs)

The reader and exporters use this application's own models and MessagePack
reader, without loading KKManager or any embedded plugin assembly. Format
identifiers and personality labels follow the reference. The original header
version is retained separately from the compatible CSV version projection.

## Boundaries and storage

- Gallery scans retain summaries, not expanded plugin payloads. Complete
  Parameter/About/KKEx values are read when the detail panel is expanded and
  released on card changes or navigation away. Clothing/accessory bodies and
  face PNGs are not decoded as metadata.
- Character and coordinate summaries use `chara_metadata.v2.json` and
  `coord_metadata.v2.json`. Old cache files are not imported or deleted; this
  naturally rebuilds the new fields. Legacy `TryParse` return values retain
  their original record equality; `TryParseWithDetails` supplies the new summary.
- MessagePack values are explicit typed nodes, never CLR type activation.
  Integers are decimal strings, binary values use Base64, extension values keep
  their type code, and maps retain arbitrary typed keys as entry lists.
- Expansion is limited to 64 MiB per block, depth 64 and 100,000 nodes. Unreadable
  blocks retain their size and diagnostic and mark the document partial. The
  JSON export and filename identify partial data; no silent truncated expansion
  is presented as complete. UI tree branches are populated in pages of 100.
- CSV uses UTF-16 LE with BOM, the reference's 14-column order and size display,
  and proper quoting for commas, quotes and newlines. Missing-mod columns are
  empty because installed mods are not inspected. Full nested data uses JSON
  schema version 1. Exports use a sibling temporary file followed by replacement.
- Coordinate cards using the common marker do not prove KK versus KKS origin.
  No origin is inferred. Legacy permissive coordinate marker matching remains.

## Original scan extraction source index (historical)

Before positions refer to the working tree after the thumbnail extraction,
including the pre-existing discovery changes. All ViewModel paths are under
`ViewModels/`.

| Owner | Before start / parse / queue | After start / queue |
| --- | --- | --- |
| GalleryViewModel.cs | 306-338 / 340-360 / 472-497 | 314 / 432 |
| CharacterGalleryViewModel.cs | 147-182 / 184-208 / 398-420 | 162 / 358 |
| CoordinateGalleryViewModel.cs | 124-156 / 158-178 / 290-311 | 139 / 257 |

Common implementation: `KoikatsuSceneGallery.Core/Services/MetadataScanCoordinator.cs`,
Start at 30, Queue at 60, ParseOneAsync at 83, Cancel at 107 and Dispose at 119.
Cache application, parsing, dispatch and completion retain their ordering.
Character version-index updates remain in the owner's apply callback; scene
refresh gating and the 750ms UI timers remain in the ViewModels.

One tested lifecycle correction accompanies the extraction: completion checks
the scan owner's token in addition to the pipeline token. Parallel.ForEachAsync
can release its linked token before queued UI callbacks run; checking only that
token allowed an old completion to alter a restarted scan's pending count.
Existing result-dispatch cancellation timing and dispatcher rejection behavior
are otherwise unchanged. Detail-panel reads independently reject stale results.

## Original extraction verification (historical)

Release tests: 203 passed. Existing assertions remain intact. New fixtures cover
format variants, optional and damaged data, bounds, arbitrary MessagePack types,
summary queries, CSV/JSON, export failure, cancellation and scan concurrency.
Windows x64 Release build: passed with 0 warnings and 0 errors. All three
resource files have the same 21 new metadata keys without duplicate keys.
The working Graft graph was refreshed through MCP; the standalone `graft`
command is not available in this shell.

Not yet manually verified: real cards compared against KKManager, tree layout
and keyboard behavior, rapid card/page switching, live gallery filters during
scans, save-dialog cancellation and visible export retry. Automated Core tests
and source comparison do not establish these UI/runtime behaviors.

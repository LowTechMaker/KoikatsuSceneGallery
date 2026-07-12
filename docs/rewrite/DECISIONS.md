# Task 1 decisions

## Scope and assumptions

- This section records Task 1 decisions only. Later stages begin only after Task 1 passes its gate and is committed.
- Existing namespaces stay unchanged so the WinUI project consumes the extracted types through a project reference without source-level API changes.
- The plugin SDK remains a separate assembly and its public API is untouched.
- Characterization fixtures are generated entirely in test code. No real card, image, configuration, cache, or user-library data is included.

## Core extraction boundary

The Core project targets plain `net10.0`. It contains the six named helpers, the five named parsers/classifiers, the four metadata/source model files that have no WinUI dependency, and `ResolutionOption`, which was split from `SceneCard.cs` without changing its namespace or behavior.

Models that inherit `ObservableObject`, contain `BitmapImage`, represent UI state, or depend on plugin/UI orchestration remain in the WinUI project. This is the smallest boundary that gives the parsing and path logic a WinUI-free test surface.

## Compatibility decisions

- `MessagePack` remains version `3.1.7`, matching the existing application dependency. No serialized schema or parser field name changed.
- `Microsoft.Web.WebView2` is pinned to `1.0.4078.44`, the version recorded in the existing `obj/project.assets.json` before Task 1.
- Internal helper visibility is preserved. The app and tests receive internal access through `InternalsVisibleTo` rather than widening production APIs after the helpers move to a separate assembly.
- The solution includes the app, its automatically discovered PluginSdk project dependency, Core, and Tests. Building the app still builds PluginSdk through the existing project reference.
- The ignored local `KoikatsuSceneGallery.Avalonia` tree is excluded from the WinUI project's default SDK globs. Its files are neither modified nor included in Task 1.

## Test strategy

Synthetic PNGs reproduce only the byte layout consumed by the current code. Synthetic character-card MessagePack blocks use the existing field names and offsets. Tests intentionally preserve edge behavior such as `PngHelper` not validating the PNG signature, case-sensitive filename patterns, permissive negative resolution values, marker substring matching, exception-to-null parser behavior, and platform-specific invalid filename characters.

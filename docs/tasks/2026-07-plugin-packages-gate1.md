# Plugin packages and shared infrastructure — Gate 1

## Status

Gate 1 is complete. The application repository can build three NuGet packages, validate them through an isolated local feed, and publish either the SDK family or Common family through one GitHub Actions workflow. No plugin repository reference was changed, no fallback DLL was removed, and no common implementation was adopted by a plugin in this gate. No commit or push was performed.

## Package shapes

`SceneGallery.PluginSdk` is a normal binary package at version `1.0.0`, targeting `net10.0`. Its assembly version is fixed at `1.0.0.0` for 1.x. The canonical pack script and package workflow derive FileVersion from the numeric SemVer core; a simulated `1.9.0` pack produced AssemblyVersion `1.0.0.0`, FileVersion `1.9.0.0`, and InformationalVersion `1.9.0`.

`SceneGallery.PluginCommon` and `SceneGallery.PluginCommon.Secrets` are source-only packages at version `0.1.0`. Explicit nuspec files place three infrastructure sources and one DPAPI source respectively below `contentFiles/cs/any/SceneGallery.PluginCommon`, with `buildAction="Compile"`. They contain no `lib` or runtime DLL. Every supplied type is internal. The Secrets package carries an exact `System.Security.Cryptography.ProtectedData` dependency at `10.0.9`.

The Common sources are package candidates only at this gate. Plugin-local RateLimiter, cache persistence, and DPAPI implementations remain active and unchanged until their later extraction gates.

## Build and publishing infrastructure

`scripts/Pack-PluginPackages.ps1` restores through the repository-owned nuget.org config, packs all three packages, validates SemVer inputs, and emits a local feed under `artifacts/local-feed` by default. `scripts/Test-PluginPackages.ps1` restores a fresh consumer into an isolated packages directory, builds with Nullable enabled and warnings as errors, inspects `project.assets.json`, and rejects an incorrect contentFiles path, non-Compile build action, or copied SDK/Common runtime DLL.

`.github/workflows/packages.yml` accepts `sdk-v*` and `common-v*` tags or a manual package-family/version input. SDK and Common versions are independent; the Common tag publishes both source packages at the same version. The workflow has only `contents: read` and `packages: write` permissions and publishes to `https://nuget.pkg.github.com/LowTechMaker/index.json`.

The WinUI application project now excludes `PluginCommon/**`, `PluginCommon.Secrets/**`, `eng/PackageValidation/**`, and generated `artifacts/**` from its root default item globs. This is required because both nested project `obj` files and restored contentFiles are C# sources that would otherwise be compiled into the application.

## Gate 1 verification

The local package validation produced all three nupkgs. The consumer build completed with zero warnings and zero errors under the same implicit C# language version, Nullable, and implicit-using settings used by the Windows plugins. The restored target graph contained three Common and one Secrets content files, all under `contentFiles/cs/any` with Compile build action. Consumer output contained no `SceneGallery.PluginSdk.dll` or Common runtime assembly.

Standalone-shape restores and tests passed as follows: Pixiv Authors 29/29, BepisDB 58/58, and Fanbox 27/27 used a deliberately missing `SceneGalleryAppDir` and therefore their tracked fallback SDK DLLs. GitHubReleaseUpdate 5/5 used the current application SDK checkout because its repository has no fallback DLL. The main application tests passed 79/79. After restoring the application, smoke project, and all four plugins for `win-x64`, plugin smoke tests passed 5/5. `Publish-WithPlugins.ps1 -NoRestore` published the application and all four plugin directories successfully with `plugins.manifest.json`.

The remote GitHub Packages workflow was not executed because this task forbids push and tag creation. Package visibility and Actions access for the four plugin repositories therefore remain manual publication prerequisites.

## Mandatory manual push and publication order

The order is strict. First push the KoikatsuSceneGallery repository changes so `.github/workflows/packages.yml` exists remotely. Next publish `SceneGallery.PluginSdk` `1.0.0`, `SceneGallery.PluginCommon` `0.1.0`, and `SceneGallery.PluginCommon.Secrets` `0.1.0`; verify they can be restored and grant GitHub Actions package access to all four plugin repositories. Only after those packages are available may the four plugin repositories be pushed with their PackageReference changes. Reversing this order causes plugin CI restore failures because the referenced packages do not yet exist.

## Remaining gates

Gate 2 switches all four plugin and test projects to the exact SDK PackageReference, applies GitHub Packages source mapping, uses `ExcludeAssets="runtime"` in plugin projects, removes the three tracked fallback DLLs, and removes GitHubReleaseUpdate's dead fallback branch. Later gates adopt the source-only RateLimiter, debounced persistence, and DPAPI packages one at a time, preserving cache schema, TTL, keys, comparer, release single-DLL shape, PluginLoadContext behavior, and the deferred Fanbox split.

The GCC `h5i` executable was not available in PATH, so context initialization could not be performed. Existing user changes in `AGENTS.md`, `MainWindow.xaml.cs`, `Pages/ImportPage.xaml.cs`, `docs/REFACTOR_PLAN_BACKUP.md`, and other user-owned untracked files were not modified.

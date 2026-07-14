# Plugin packages and shared infrastructure — Gate 2

## Status

Gate 2 is complete locally. All four plugin repositories and their test projects consume the exact `SceneGallery.PluginSdk` package version `1.0.0`. The three tracked fallback DLLs are removed, GitHubReleaseUpdate no longer contains an unreachable fallback reference or checks out the application SDK in CI, and every repository has production source mapping for GitHub Packages. No common source-only implementation was adopted, no commit or push was performed, and Gate 3 has not started.

## Package references and runtime ownership

Each plugin project now has a `PackageReference` with `Version="1.0.0"`, `ExcludeAssets="runtime"`, and `PrivateAssets="all"`. Each test project has its own exact `1.0.0` reference without excluding runtime assets, so the test host can load SDK contract types while the plugin output remains contract-free. The plugin projects retain `SceneGalleryAppDir` only for the existing development copy targets; it no longer selects an SDK reference.

The conditional sibling `ProjectReference` and DLL `HintPath` branches were removed from all eight project files. PixivAuthorsPlugin, BepisDbPlugin, and FanboxWebView2Plugin no longer contain their tracked `lib/SceneGallery.PluginSdk.dll`. GitHubReleaseUpdate never had a tracked fallback DLL, so its false conditional branch was removed together with the now-unused nested `KoikatsuSceneGallery/**` compile exclusion.

The final `win-x64` outputs contain neither `SceneGallery.PluginSdk.dll` nor a `SceneGallery.PluginSdk` entry in any plugin `.deps.json`. The combined publication likewise contains no SDK copy below `Plugins/`; the host remains the only runtime source of the contract assembly and `PluginLoadContext` is unchanged.

## Source mapping and credentials

All four plugin repositories now contain the same root `NuGet.config`. It clears inherited package sources, defines nuget.org and `SceneGalleryGitHub`, maps the general `*` pattern to nuget.org, and maps the more specific `SceneGallery.*` pattern only to GitHub Packages at `https://nuget.pkg.github.com/LowTechMaker/index.json`.

The build and release workflows request `packages: read`. At job scope they provide `NuGetPackageSourceCredentials_SceneGalleryGitHub` with `github.actor` and `GITHUB_TOKEN`, leaving no credential in source control. GitHubReleaseUpdate's build and release workflows now use a single plugin checkout and no longer pass `SceneGalleryAppDir` to obtain the SDK.

The remote packages do not yet exist, so this gate cannot honestly prove a GitHub Packages restore. Pre-publication verification instead explicitly selected `eng/PackageValidation/NuGet.config`, restored into fresh repository-owned package directories below `artifacts/gate2-package-restore`, and consumed `artifacts/local-feed`. Every plugin `project.assets.json` records `SceneGallery.PluginSdk/1.0.0`, the isolated package path, and no `PluginSdk.csproj`. This is a local-feed equivalent of the standalone CI shape; remote authentication and Actions access remain post-publication checks.

## Verification

The pre-change baseline passed Pixiv Authors 29/29, BepisDB 58/58, Fanbox 27/27, and GitHubReleaseUpdate 5/5. After the package-only switch, the same four suites passed with identical counts. Main application tests passed 79/79.

All four plugin projects were restored for `win-x64` from the local feed and built in Release with `DeployPluginToApp=false`; Pixiv, Bepis, Fanbox, and GitHubReleaseUpdate each completed with zero warnings and zero errors. The main application Release x64 build completed with zero warnings and zero errors after a Release/x64 restore that included the ReadyToRun crossgen2 pack. Plugin load smoke tests passed 5/5. `Publish-WithPlugins.ps1 -NoRestore` completed successfully and produced all four plugin directories plus `plugins.manifest.json`.

The first sandboxed restore attempt could not read the user's `AppData/Roaming/NuGet/NuGet.Config`; rerunning the same repo-owned restore with the required filesystem approval resolved it. The first baseline tests were launched in parallel while all four projects still referenced the same SDK project, which caused a transient compiler lock in the shared SDK `obj` directory; sequential baseline execution passed, and the package-only projects no longer share that build output. The first main Release build lacked a crossgen2 pack because restore had not used Release `PublishReadyToRun` properties; restoring with `Configuration=Release`, `Platform=x64`, and `PublishReadyToRun=true` fixed the verification graph without a source change.

## Manual publication prerequisites

The Gate 1 push order remains mandatory. Push the KoikatsuSceneGallery repository first, let `packages.yml` exist remotely, publish SDK `1.0.0` and both Common `0.1.0` packages, verify package availability, and grant all four plugin repositories GitHub Actions access. Only then push the four plugin repositories. A local developer restore against the committed configuration requires `NuGetPackageSourceCredentials_SceneGalleryGitHub` backed by a classic PAT with `read:packages`.

Because no package was published in this gate, the following are deliberately unverified: restore from the real GitHub Packages endpoint, package visibility, repository Actions access, `GITHUB_TOKEN` authentication in each plugin workflow, and a clean remote CI checkout. Those checks belong to the user's publication sequence and must not be inferred from the successful local-feed run.

## Next gate

Gate 3 may begin only after this gate is reviewed. Adopt the source-only infrastructure in the locked order: RateLimiter, debounced persistence, then DPAPI. Run the full gate after each extraction and preserve SDK public signatures, cache schemas, TTLs, keys, comparers, Fanbox's three-key mutation followed by one `MarkDirty()`, plugin release shape, `PluginLoadContext`, and the deferred Fanbox split.

Existing user-owned `.claude/` directories and the main repository changes in `AGENTS.md`, `MainWindow.xaml.cs`, `Pages/ImportPage.xaml.cs`, `docs/REFACTOR_PLAN_BACKUP.md`, and the unrelated untracked text file were not modified.

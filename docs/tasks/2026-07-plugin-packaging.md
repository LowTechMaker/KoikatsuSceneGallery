# Plugin packaging and load smoke tests

## Outcome

SceneGallery now has one explicit publishing entry point that builds the WinUI application and the four plugin sibling repositories into a single release directory. The process no longer depends on plugin post-build copies. Those copies remain enabled by default for F5 development, but the release script disables them with `DeployPluginToApp=false` and sends each plugin directly to its final isolated directory.

The chosen source model is option B: the caller prepares a workspace containing five sibling Git repositories. The script never clones, checks out, pulls, commits, or otherwise changes Git state. This was chosen instead of option A because the plugins have independent repositories and release histories, and silently fetching mutable remote branches would make a local release depend on network state. The generated manifest provides traceability without introducing submodules or an automatic source acquisition policy.

## Expected workspace layout

The directory names are part of the publishing and smoke-test convention:

```text
SceneGallery/
├── KoikatsuSceneGallery/
│   ├── KoikatsuSceneGallery.csproj
│   ├── KoikatsuSceneGallery.PluginSmokeTests/
│   └── scripts/Publish-WithPlugins.ps1
├── PixivAuthorsPlugin/
│   └── SceneGallery.Plugin.PixivAuthors.csproj
├── BepisDbPlugin/
│   └── SceneGallery.Plugin.BepisDb.csproj
├── FanboxWebView2Plugin/
│   └── SceneGallery.Plugin.FanboxWebView2.csproj
└── GitHubReleaseUpdatePlugin/
    └── SceneGallery.Plugin.GitHubReleaseUpdates.csproj
```

## Publishing

Run the following command from the `KoikatsuSceneGallery` repository:

```powershell
.\scripts\Publish-WithPlugins.ps1
```

The default is a Release, self-contained `win-x64` unpackaged application. Output is written to `artifacts/release/win-x64`. `-Configuration`, `-RuntimeIdentifier`, and `-OutputDirectory` can override those values. The output directory must remain inside the `SceneGallery` workspace so the script cannot recursively remove an unrelated path. When packages were restored earlier, `-NoRestore` performs an offline build from the current restore state.

The expected release shape is:

```text
artifacts/release/win-x64/
├── KoikatsuSceneGallery.exe
├── SceneGallery.PluginSdk.dll
├── KoikatsuSceneGallery.Core.dll
├── plugins.manifest.json
└── Plugins/
    ├── PixivAuthors/
    │   ├── SceneGallery.Plugin.PixivAuthors.dll
    │   ├── SceneGallery.Plugin.PixivAuthors.deps.json
    │   └── plugin-owned dependencies
    ├── BepisDb/
    │   ├── SceneGallery.Plugin.BepisDb.dll
    │   ├── SceneGallery.Plugin.BepisDb.deps.json
    │   └── WebView2 dependencies
    ├── Fanbox/
    │   ├── SceneGallery.Plugin.Fanbox.dll
    │   ├── SceneGallery.Plugin.Fanbox.deps.json
    │   └── WebView2/Windows App SDK dependencies
    └── GitHubReleaseUpdates/
        ├── SceneGallery.Plugin.GitHubReleaseUpdates.dll
        └── SceneGallery.Plugin.GitHubReleaseUpdates.deps.json
```

Plugin directories must not contain `SceneGallery.PluginSdk.dll` or `KoikatsuSceneGallery.Core.dll`. The script treats either duplicate as a packaging error. `PluginLoadContext` continues returning the SDK contract assembly to the default AssemblyLoadContext, so the app and plugins share one contract type identity.

`plugins.manifest.json` records the application repository and every expected plugin. Each present repository includes its workspace-relative path, current commit hash, branch, and dirty flag. Plugin entries also record whether the sibling was present and included, plus the relative output directory. A dirty repository emits a warning but does not stop publishing. Git queries use a command-local `safe.directory` setting because Windows sandbox and host accounts can otherwise trigger Git's dubious-ownership protection; the script does not modify global Git configuration.

If one or more plugin siblings are absent, the script lists every missing directory and prompts before continuing. `-AllowMissing` (also aliased as `-allow-missing`) skips the prompt and publishes the available plugins. In CI, missing siblings fail unless `-AllowMissing` is explicit, which prevents a non-interactive release from hanging or silently omitting plugins.

## Smoke tests

Run the plugin tests after restoring the test project and all present plugin projects for `win-x64`:

```powershell
dotnet test .\KoikatsuSceneGallery.PluginSmokeTests\KoikatsuSceneGallery.PluginSmokeTests.csproj `
  -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
```

The test project publishes each plugin with `--no-restore` into a unique temporary `Plugins/<name>` directory, rejects duplicate SDK/Core assemblies, and loads it with the production `PluginService` and `PluginLoadContext` source files. Four theory cases independently verify loaded status, name, version, assembly metadata, and every capability interface declared by that plugin. A fifth test publishes all four plugins into one tree and confirms that one `PluginService` instance enumerates all four without shared AssemblyLoadContext conflicts.

When a sibling project is absent, its individual theory case is reported as skipped with the missing repository name. The combined four-plugin case is also skipped and lists all missing siblings. The test project does not reference the WinUI executable project: doing so runs the Windows App SDK module initializer before xUnit reaches the test and fails on machines without a registered runtime. Instead, the test project compile-links the exact production loader source and references only the pure Plugin SDK project.

The tests do not call Pixiv, Fanbox, BepisDB, GitHub, or any other external service. Plugin `Initialize` methods are exercised, but capability methods that fetch remote data are not invoked. The Fanbox and BepisDB plugins are therefore checked for assembly resolution and registration only; the WebView2 runtime, browser environment, authentication, cookies, UI, and actual network operations are outside this smoke test. This is intentional so the test can run after an offline restore and in CI without a WebView2 runtime initialization step.

## CI and release integration

`.github/workflows/build.yml` contains a Windows plugin smoke-test job. It checks the application and four plugin repositories out as siblings, restores each project, then runs the smoke tests with `--no-restore`. The existing Linux Core tests and Windows application build remain separate.

`.github/workflows/release.yml` now checks out the same five-repository layout and calls `Publish-WithPlugins.ps1`. Its zip is created from `artifacts/release/win-x64`, so the release archive contains the application, all four plugin directories, and `plugins.manifest.json` from the same invocation.

## Known limitations and lessons

The release is reproducible from the five local checkouts, not from the main repository alone. Rebuilding an old release requires restoring the five commit hashes recorded in its manifest. The workflow currently checks out each plugin repository's default branch and relies on the manifest for exact provenance; it does not impose a cross-repository tag policy.

BepisDB currently emits an existing MSBuild warning about conflicting `WindowsBase` references introduced by WebView2's WPF assets. Publishing and the load smoke test both succeed. Package version changes were explicitly outside this task, so the warning was documented rather than suppressed or fixed.

The first end-to-end script run encountered Git's dubious-ownership check on one sibling repository. Using `git -c safe.directory=<repo>` for each read-only query solved it without changing user settings. The first test-host design referenced the WinUI application and failed before test execution because the Windows App SDK bootstrapper attempted COM activation. Compile-linking the production loader into the test project avoids UI/runtime initialization while still testing the actual loader implementation.

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

## CI and release integration

The plugin packaging smoke-test project and its cross-repository CI job were retired in September 2026. CI now runs the Core test suite and the Windows application build only.

`.github/workflows/release.yml` publishes the self-contained Windows application directly. Its zip is created from `artifacts/release/win-x64` and does not bundle plugins.

## Known limitations and lessons

The release is reproducible from the five local checkouts, not from the main repository alone. Rebuilding an old release requires restoring the five commit hashes recorded in its manifest. The workflow currently checks out each plugin repository's default branch and relies on the manifest for exact provenance; it does not impose a cross-repository tag policy.

BepisDB currently emits an existing MSBuild warning about conflicting `WindowsBase` references introduced by WebView2's WPF assets. Publishing and the load smoke test both succeed. Package version changes were explicitly outside this task, so the warning was documented rather than suppressed or fixed.

The first end-to-end script run encountered Git's dubious-ownership check on one sibling repository. Using `git -c safe.directory=<repo>` for each read-only query solved it without changing user settings. The first test-host design referenced the WinUI application and failed before test execution because the Windows App SDK bootstrapper attempted COM activation. Compile-linking the production loader into the test project avoids UI/runtime initialization while still testing the actual loader implementation.

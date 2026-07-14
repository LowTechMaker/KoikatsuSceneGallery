# Plugin release versioning and GitHub asset selection

## Outcome

All four plugin repositories now use the release tag as the single source of truth for release builds. Each release workflow removes one optional `v` prefix, validates the remaining value as SemVer, passes it to MSBuild through `-p:Version`, and publishes a DLL whose filename contains the same normalized version. An invalid tag fails before build or release creation.

The projects retain a clearly non-release default for local builds:

```xml
<Version Condition="'$(Version)' == ''">0.0.0-dev</Version>
```

This is an overridable default. A workflow or local verification command that supplies `-p:Version=1.2.3` wins over the project value.

## Workflow changes

The release workflows in PixivAuthorsPlugin, BepisDbPlugin, FanboxWebView2Plugin, and GitHubReleaseUpdatePlugin use the same PowerShell normalization and strict SemVer regular expression. Accepted examples include `v1.2.3`, `1.2.3`, and `1.2.3-beta.1+build.5`. Values such as `1.2`, `01.2.3`, `v1.2.3.4`, and `release` fail with an explicit error.

Release builds use the equivalent of:

```powershell
dotnet build -c Release `
  -p:Version=$version `
  -p:IncludeSourceRevisionInInformationalVersion=false `
  -p:DeployPluginToApp=false
```

`IncludeSourceRevisionInInformationalVersion=false` is required because the .NET SDK otherwise changes `1.2.3` into `1.2.3+<commit-sha>`. GitHubReleaseUpdate keeps its existing structural difference: its build also passes `SceneGalleryAppDir` because CI checks out the application SDK inside that repository.

## Release asset naming

The release DLL convention is `<assembly-name>-<normalized-version>.dll`:

```text
SceneGallery.Plugin.PixivAuthors-1.2.3.dll
SceneGallery.Plugin.BepisDb-1.2.3.dll
SceneGallery.Plugin.Fanbox-1.2.3.dll
SceneGallery.Plugin.GitHubReleaseUpdates-1.2.3.dll
```

The original build output is copied to this versioned name before `softprops/action-gh-release` uploads it. The assembly metadata and the asset filename therefore come from the same normalized workflow value.

## GitHubReleaseUpdate behavior

GitHubReleaseUpdate now opts into its own update provider through:

```text
PluginUpdateUrl=https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin
```

Asset selection derives the assembly suffix from the plugin display name by removing non-alphanumeric characters, following the existing `SceneGallery.Plugin.<Name>` convention. It accepts only an exact case-insensitive match for either `<assembly-name>.dll` or `<assembly-name>-<release-version>.dll`. The unversioned form remains supported for older releases.

There is no longer a fallback to the first DLL, the first arbitrary asset, a URL that merely ends in `.dll`, or the release HTML page. If no exact asset exists, the provider logs `No usable asset`, returns `null`, and the host skips that plugin update.

The plugin has an xUnit project and its build workflow runs `dotnet test`. Tests use an injected `HttpMessageHandler`; no test calls GitHub or another network service. Coverage includes self-update metadata, leading-`v` normalization, exact unversioned matching, exact versioned matching in a multi-DLL release, and skipping a release with no matching asset.

## Local verification

The workflow normalization logic was exercised with valid and invalid sample tags. Each plugin was then built with simulated version `7.8.9` and release-equivalent properties. All four DLLs reported FileVersion `7.8.9.0`, ProductVersion `7.8.9`, and generated AssemblyInformationalVersion `7.8.9`.

The final verification results were:

```text
GitHubReleaseUpdate tests:  5 passed, 0 failed, 0 skipped
Main application tests:    79 passed, 0 failed, 0 skipped
Plugin load smoke tests:    5 passed, 0 failed, 0 skipped
Full plugin publication:    succeeded with four plugin directories and plugins.manifest.json
```

Before smoke tests or `Publish-WithPlugins.ps1 -NoRestore`, restore the smoke project, application, and every plugin project for `win-x64`. Running a non-RID unit test can replace a plugin's `project.assets.json` with a graph that lacks `win-x64`, causing `NETSDK1047` during the subsequent publish.

Run `Publish-WithPlugins.ps1` under PowerShell 7 (`pwsh`). Windows PowerShell 5.1 does not expose `System.IO.Path.GetRelativePath` and fails before the build begins.

## Known tag and build pitfalls

Fanbox currently has an anomalous remote `v2.0.0` tag pointing at the same old commit as `0.0.1`, with no corresponding GitHub release, while the actual latest release is `0.0.3`. The tag is valid SemVer, so the workflow correctly normalizes it to `2.0.0`; SemVer validation cannot determine whether a syntactically valid version reflects product progress or already has a release. The remote tag therefore requires the planned manual cleanup and is intentionally not modified by this task.

PowerShell does not support an open-ended `$tag[1..]` slice. Prefix removal uses `$tag.Substring(1)`.

BepisDb continues to emit its pre-existing MSBuild warning about conflicting `WindowsBase` versions from WebView2 WPF assets. The release publish and smoke tests still succeed, and package changes remain outside this task.

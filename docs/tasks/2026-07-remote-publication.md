# Plugin package and remote release publication

## Outcome

The GitHub Packages migration and remote publication sequence completed successfully on 2026-07-14. The application package workflows were published first, package access was granted to all four plugin repositories, and each plugin then completed a clean GitHub Actions restore, build, and test before the next repository was pushed.

The publication branch was merged to `master` by [PR #7](https://github.com/LowTechMaker/KoikatsuSceneGallery/pull/7) at merge commit `78c97b5a8a3e210005157369f78b558ce527d090`. All three PR checks and all three jobs in the subsequent `master` push workflow were green, including the cross-repository plugin smoke test.

The final end-to-end release verification used GitHubReleaseUpdatePlugin tag `v0.0.2`, pointing to commit `e2a1be07b3011c66700db681fd7b1878e3ba792d`. The tag was created by the preceding Codex session after release authorization, but that session was interrupted before it could report the completed verification. This session detected and retained the existing correct tag, then completed the workflow, release, asset, hash, and embedded-version checks without deleting or recreating the tag. The workflow normalized the tag to `0.0.2`, accepted it as SemVer, supplied `-p:Version="0.0.2"`, created a GitHub release, and uploaded a versioned single-DLL asset. The downloaded asset reported the expected file and informational versions.

## Remote workflow results

| Repository and event | Run | Result | Evidence |
| --- | ---: | --- | --- |
| KoikatsuSceneGallery branch push | none | not triggered | `build.yml` only includes `master`; `refactor/test-safety-core` therefore had no CI run. The previously completed local full verification remained the substitute, and the trigger was not changed. |
| KoikatsuSceneGallery `sdk-v1.0.0` | [29326603466](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/29326603466) | green | Packed and pushed `SceneGallery.PluginSdk.1.0.0.nupkg`. |
| KoikatsuSceneGallery `common-v0.1.0` | [29326758460](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/29326758460) | green | Packed and pushed `SceneGallery.PluginCommon.0.1.0.nupkg` and `SceneGallery.PluginCommon.Secrets.0.1.0.nupkg`. |
| PixivAuthorsPlugin `master` push | [29333577371](https://github.com/LowTechMaker/pixiv-data-plugin/actions/runs/29333577371) | green | Clean GitHub Packages restore, build, and 29/29 tests. |
| BepisDbPlugin `master` push | [29333693256](https://github.com/LowTechMaker/BepisDbPlugin/actions/runs/29333693256) | green | Clean GitHub Packages restore, build, and 58/58 tests. |
| FanboxWebView2Plugin `master` push | [29333783014](https://github.com/LowTechMaker/FanboxWebView2Plugin/actions/runs/29333783014) | green | Clean GitHub Packages restore, build, and 27/27 tests. |
| GitHubReleaseUpdatePlugin `master` push | [29334143659](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/29334143659) | green | Clean GitHub Packages restore, build, and 5/5 tests. |
| GitHubReleaseUpdatePlugin `v0.0.2` release | [29335156555](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/29335156555) | green | Version normalization, SemVer validation, release build, asset packaging, release creation, and upload all succeeded. |
| KoikatsuSceneGallery PR #7 | [PR #7](https://github.com/LowTechMaker/KoikatsuSceneGallery/pull/7) | merged, green | Merged `refactor/test-safety-core` to `master` as `78c97b5a`; PR workflow run [29338811744](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/29338811744) passed `core-tests`, `build (x64, Release)`, and `plugin-smoke-tests`. |
| KoikatsuSceneGallery `master` merge push | [29340403927](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/29340403927) | green | At merge SHA `78c97b5a`; `core-tests`, `build (x64, Release)`, and `plugin-smoke-tests` all completed successfully. |

The package access configuration covered `PixivAuthorsPlugin`, `BepisDbPlugin`, `FanboxWebView2Plugin`, and `GitHubReleaseUpdatePlugin` for all three packages. The four clean restores using each repository's `GITHUB_TOKEN` provide the end-to-end authorization check; no plugin CI received a package 403 or missing-package error.

## GitHubReleaseUpdatePlugin release evidence

The preceding session created `v0.0.2` at `e2a1be07b3011c66700db681fd7b1878e3ba792d` and was interrupted before its verification report. This session first confirmed that the existing tag already pointed to the intended `master` HEAD and therefore neither deleted nor recreated it. Release workflow run [29335156555](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/29335156555) had been triggered by that tag push and completed successfully.

The Release workflow checked out `v0.0.2` at the expected commit. The `Determine version tag` log received `v0.0.2`, removed the leading `v`, evaluated the normalized `0.0.2` against the shared SemVer regular expression, and completed successfully. The Build step then executed:

```text
dotnet build SceneGallery.Plugin.GitHubReleaseUpdates.csproj -c Release -p:Version="0.0.2" -p:IncludeSourceRevisionInInformationalVersion=false -p:DeployPluginToApp=false
```

The build restored from the configured package sources and completed with zero warnings and zero errors. The packaging step copied the output to `SceneGallery.Plugin.GitHubReleaseUpdates-0.0.2.dll`. The release action created release ID `353802178` for tag and name `v0.0.2`, uploaded the versioned DLL, and finalized the release at <https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/releases/tag/v0.0.2>.

GitHub reported the following asset metadata:

```text
Asset ID:              476712271
Name:                  SceneGallery.Plugin.GitHubReleaseUpdates-0.0.2.dll
State:                 uploaded
Size:                  12,800 bytes
SHA-256:               7b652d7a756abe088af0e0215b6c86e9d59af81cd1de0ddf12fcf97cec35bf14
Release draft:         false
Release prerelease:    false
Published:             2026-07-14T13:07:37Z
```

The asset was downloaded from the release and inspected independently. PowerShell calculated its SHA-256 and Windows file version, while `System.Reflection.Metadata` read `AssemblyInformationalVersionAttribute` directly from the PE metadata without loading the plugin. The calculated SHA-256 exactly matched the GitHub digest, and its embedded version data was:

```text
FileVersion:           0.0.2.0
InformationalVersion:  0.0.2
ProductVersion:        0.0.2
```

The asset filename therefore contains the normalized `0.0.2` version and both required assembly version assertions passed. The temporary download stayed outside all repositories, and no validation artifact is included in this commit.

## Cleanup and observations

After all four plugin push workflows were green, the obsolete FanboxWebView2Plugin remote tag `v2.0.0` was deleted. A subsequent `git ls-remote` query returned no matching tag.

The successful workflows emitted the existing GitHub-hosted runner annotation that `actions/checkout@v4`, `actions/setup-dotnet@v4`, and, for the release run, `softprops/action-gh-release@v2` target Node.js 20 and were forced onto Node.js 24. This was a non-failing deprecation annotation; no workflow was changed as part of remote publication.

No source, package mapping, workflow trigger, SDK public signature, plugin release shape, cache behavior, or plugin load-context rule was changed during remote publication. The sequence finished with all package workflows, all four plugin CI workflows, and the end-to-end release workflow green.

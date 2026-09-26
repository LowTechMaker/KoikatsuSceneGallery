# 最小插件範例

此目錄可複製為獨立 repository。它只查本地假資料：`example_42` 對應 Example Author，不會連線或寫使用者資料。

依賴方向：ExamplePlugin → ExampleAuthorService → FakeAuthorSource；ExamplePlugin → ExampleFolderParser。Parser 純函式；唯一 public type 是 SDK 入口，其他型別 internal。無背景 producer，所以不引入不需要的生命週期框架。

| 擴充需求 | 位置 |
| --- | --- |
| 修改檔名／資料夾規則 | [ExampleFolderParser](ExampleFolderParser.cs) |
| API 與 mapping | [ExampleAuthorService / FakeAuthorSource](ExampleAuthorService.cs) |
| SDK capability、組裝／所有權 | [ExamplePlugin](ExamplePlugin.cs) |
| 離線驗證 | [ExamplePluginTests](SceneGallery.Plugin.Example.Tests/ExamplePluginTests.cs) |

建立第五個插件時同步更名 assembly、namespace、plugin Name、ProviderId、測試 project 和 InternalsVisibleTo；定義自己的資料格式與失敗政策。新增公開能力須加 signature/capability baseline 和對应回歸測試。不要複製其他站點的 Cookie、TTL、retry 政策。

SDK 1.3.0 必須 ExcludeAssets=runtime、PrivateAssets=all；AssemblyVersion 維持 1.0.0.0。1.3.0 的 `IPluginHost.Language` 與可選 `ITagDictionaryProvider` 可在需要時使用，範例不實作未用到的能力。測試輸出需要 SDK DLL，正式插件輸出不得攜帶 SDK／測試依賴。範例未列入主程式編譯或四插件發佈清單。

架構改動先記錄決策、相容性與對應測試；所有驗證走 [Verify-Plugin](eng/Verify-Plugin.ps1)。

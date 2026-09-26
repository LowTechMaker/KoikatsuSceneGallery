# 四插件重構整體交接

交接版本 v3，2026-09-27。先讀 [架構與決策](plugin-architecture.md) 和 [工程規範](plugin-conventions.md)。

## 結果與責任

四插件維持獨立 repo／原 settings/cache schemas。最初計畫固定 SDK1.0.0；使用者在 2026-09-26 選擇保留工作樹中較新的 SDK 套件 1.3.0 與 Pixiv tag 功能，AssemblyVersion 仍為 1.0.0.0，宿主提供唯一 runtime assembly。Bepis 的 cookie lifetime、Pixiv 的 force-refresh cache/avatar、Fanbox 的 preview persistence 與 dispatcher cleanup 已以失敗優先測試修正；GitHub updater 等價拆分 client/policy。這次重構沒有新增宿主 UI 改動、沒有通用 plugin base。

| Repo | 新責任邊界 | 必讀文件 |
| --- | --- | --- |
| BepisDbPlugin | façade / runtime / fetch coordinator / session owner / response parser / mapper | repo 根目錄 AGENTS、ARCHITECTURE、HANDOFF |
| PixivAuthorsPlugin | façade / runtime / coordinator / staged-avatar publish / SauceNao client-parser-encoder | 同上 |
| FanboxWebView2Plugin | façade / artwork service / creator catalog-resolution / ApiClient-Navigator-Host / shutdown | 同上 |
| GitHubReleaseUpdatePlugin | façade / release client / pure release policy | 同上 |

主程式新增 Common lifecycle 原語與 tests、套件驗證、最小插件範例、工作區驗證和正式 loader probe。`KoikatsuSceneGallery.csproj` 只追加新測試／工具／範例來源排除，保留既有使用者變更。

## 基線與缺陷證據

開始前已將五 repo 的 HEAD、status、staged/unstaged patch 與既有變更檔案 SHA256 存於工作區 `validation/plugin-refactor-20260922/baseline`。未 stash/reset 或覆蓋使用者內容；沒有 commit/push/發布。

- Bepis：原 cookie 提前釋放、舊請求共用、遲到 challenge/validation、disposed admission、deferred cache 六個案例先失敗再通過。
- Pixiv：原 force refresh 的 stale cache、stale finally、stale avatar 三案例，以及 disposed admission 先失敗；補 late producer flush 與 preview promotion/Dispose 交錯。保留 SDK1.3 新增的 tag capability 時，又以失敗測試重現 caller 取消後 tag producer 未計入關閉；修正為 shutdown token 擁有共用 producer，caller 只取消自身等待，並鎖定單次 fetch 與 cache 重讀。
- Fanbox：preview→save 未保存 requested alias 和 Host 額外等待先重現，再修正。2026-09-26 原生 WebView2 smoke 又發現 dispatcher 完成通知使關閉約耗 20 秒；新增重複 `Dispose` 失敗先行測試，修正後使用非同步完成通知並只讓首次關閉同步等待。WebView2 script 的 AsTask/WaitAsync/abandoned-observer 與既有 pacing 保留。
- Updater：新增行為測試在拆分前後均 29/29，無政策改動。

上述 TRX／來源搬移索引在工作區 validation 下及各 repo HANDOFF 記載的位置。歷史 2026-07 文件的 suite counts／smoke 結果不視為本次證據。

## 驗證方式與目前結果

```powershell
# 五個 sibling repo 存在；輸出使用新的隔離目錄，不部署插件
pwsh -NoProfile -File eng/Verify-PluginWorkspace.ps1

# 只有主程式 checkout 時可獨立驗證共用基礎
dotnet test PluginCommon.Tests/SceneGallery.PluginCommon.Tests.csproj -c Release
pwsh -NoProfile -File scripts/Test-PluginPackages.ps1
```

SDK1.3 最終 `Verify-PluginWorkspace.ps1` 已通過，使用新的各 repo 隔離套件 cache 與輸出（0 failed、0 skipped）：

| Suite | 通過 / 失敗 / 略過 |
| --- | --- |
| BepisDB | 102 / 0 / 0 |
| Pixiv Authors | 125 / 0 / 0 |
| Fanbox | 103 / 0 / 0 |
| GitHub Release Updates | 38 / 0 / 0 |
| Common lifecycle | 8 / 0 / 0 |
| MinimalPlugin | 8 / 0 / 0 |
| 合計 | **384 / 0 / 0** |

整合證據目錄：`artifacts/plugin-workspace/d2ba7560c24246ecb11a173b2518d9f3`。所有 plugin 的 package metadata、正式輸出與文件連結 gate 通過；SDK1.3.0、Common/Secrets0.2.0 fresh package validation 成功、0 warnings、無 SDK/Common runtime 複製；正式 PluginLoadContext 載入四插件、核對完整 capabilities，且僅一份 default-context SDK 成功。NetArch 架構閘門另外以完整 top-level 型別清單（含子命名空間）防止新類別逃離分層檢查；Fanbox 也鎖定公開方法、metadata 與設定鍵。

最新 SDK1.3 工作樹的 Windows 宿主 Release win-x64/self-contained 在工作區 `validation/plugin-refactor-20260922/host-build-sdk13` 建置成功，0 warnings／0 errors，使用 `BuildLocalPlugins=false`、`DeployPluginToApp=false`。未因 build 啟動應用程式或部署插件。

原主程式忽略所有 docs 與 AGENTS；本次僅將本 AGENTS 和三份插件架構／規範／交接文件納入可版控範圍，其餘本地工作筆記仍忽略。基線 SHA256 再比對顯示多個主程式 UI／服務檔案在工作期間另有變更，且原有 `Services/MediaCardService.cs` 現已不存在；本次重構只調整 `KoikatsuSceneGallery.csproj` 的插件來源排除，未編輯或回復那些主程式功能。完整差異清單存於工作區 `validation/plugin-refactor-20260922/baseline-differences.json`。Fanbox 原有三個未提交檔案則因本次要求直接重構，其初始快照仍在 baseline。

所有正常 pipeline 的網路測試使用 fake transport。另以 SDK1.3 最終正式 Pixiv Release DLL 跑真實 Windows/WinRT codec：自產 8×6 透明 PNG → JPEG 解碼後全白、3×2 紅色 PNG → JPEG 解碼後維持尺寸與顏色（JPEG 容差 8），兩案通過。證據在工作區 `validation/plugin-refactor-20260922/pixiv-codec-probe/results-sdk13-final/codec-result.json`，沒有讀使用者圖片、沒有網路/API key。這不涵蓋 EXIF 旋轉、UI 取消或真實 SauceNao 服務。

Fanbox 使用最終全工作區 gate 的正式 Release DLL 與最新宿主 x64 runtime 依賴、隔離 profile 啟動真實 WebView2 並關閉兩輪，重複關閉與關閉後拒絕初始化均通過；清理時間分別 0.046／0.023 秒。證據在工作區 `validation/plugin-refactor-20260922/fanbox-native-smoke/runtime-artifacts-final-workspace/native-smoke.log`。這個 smoke 沒有導覽頁面，也沒有使用或更動使用者的登入 profile。

## 人工驗證與剩餘限制

- 尚未執行真實 WebView2 登入保存、challenge 成功/timeout/cancel、age gate、idle 重啟、匯入中關閉，以及 SauceNao／Pixiv tag endpoint 的真實服務互動與宿主語言切換；離線 seams、原語測試與無導覽 native smoke 不能取代這些操作。
- 宿主 v0.4.0 正式 Release ZIP 已發布，SHA256 與 GitHub asset digest 一致；ZIP 內實際 SDK 可載入四個候選新版插件。v0.3.0 宿主無法載入新版 Pixiv，因為舊 SDK 缺少 `IArtworkMetadataRefresher` 等型別。此載入 smoke 未驗證各插件的實際網路或瀏覽器工作流程。
- 同步等待預算不能強制中止 COM 或 IO；逾時後仍追蹤 cleanup，文件不可宣稱所有 Dispose 一定在 10 秒內完成。
- 既有使用者改動若導致不相關宿主測試失敗，須分開記錄，不能為讓插件 gate 變綠修改其他功能。

## 發布與回復

使用者於 2026-09-27 同意提交、推送與發布插件新版本。宿主 [v0.4.0](https://github.com/LowTechMaker/KoikatsuSceneGallery/releases/tag/v0.4.0) 發版來源包含本次共用基礎，遠端 [Build 工作流程](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/36269424724) 已成功。套件已依序發布：[`sdk-v1.3.0`](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/36269773418) 與 [`common-v0.2.0`](https://github.com/LowTechMaker/KoikatsuSceneGallery/actions/runs/36269874403) 工作流程均通過測試，GitHub Packages 日誌確認 SDK1.3.0、Common0.2.0、Secrets0.2.0 三個套件實際推送成功。

四插件的 Build CI 均從正式 GitHub Packages 還原並執行各自的 Verify-Plugin gate，Release CI 再跑同一 gate；公開 DLL 已核對 GitHub asset SHA256、版本與宿主 v0.4.0 SDK 的載入相容性。發版後 HANDOFF 的文件提交與 Build CI 也均成功。

| 插件 | 公開版本／來源 commit | Build／Release CI | HANDOFF 提交／Build CI |
| --- | --- | --- | --- |
| BepisDB | [v0.0.5](https://github.com/LowTechMaker/BepisDbPlugin/releases/tag/v0.0.5)／`781bc35` | [Build](https://github.com/LowTechMaker/BepisDbPlugin/actions/runs/36270003931)／[Release](https://github.com/LowTechMaker/BepisDbPlugin/actions/runs/36270117793) | `7c1ad11`／[Build](https://github.com/LowTechMaker/BepisDbPlugin/actions/runs/36270329810) |
| Pixiv Authors | [v0.0.6](https://github.com/LowTechMaker/pixiv-data-plugin/releases/tag/v0.0.6)／`cd0c0a7` | [Build](https://github.com/LowTechMaker/pixiv-data-plugin/actions/runs/36270005031)／[Release](https://github.com/LowTechMaker/pixiv-data-plugin/actions/runs/36270118308) | `92c2a82`／[Build](https://github.com/LowTechMaker/pixiv-data-plugin/actions/runs/36270331243) |
| Fanbox WebView2 | [v0.0.5](https://github.com/LowTechMaker/FanboxWebView2Plugin/releases/tag/v0.0.5)／`8c08ee6` | [Build](https://github.com/LowTechMaker/FanboxWebView2Plugin/actions/runs/36270008443)／[Release](https://github.com/LowTechMaker/FanboxWebView2Plugin/actions/runs/36270133018) | `1b6fdc9`／[Build](https://github.com/LowTechMaker/FanboxWebView2Plugin/actions/runs/36270384296) |
| GitHub Release Updates | [v0.0.4](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/releases/tag/v0.0.4)／`3fd3de2` | [Build](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/36270019868)／[Release](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/36270144789) | `95afbfe`／[Build](https://github.com/LowTechMaker/GitHubReleaseUpdatePlugin/actions/runs/36270395318) |

Pixiv v0.0.6 使用 SDK1.3 新介面，最低需宿主 v0.4.0；公開 v0.3.0 宿主載入此插件會發生 `TypeLoadException`。不要把 local-feed 加入正式 config，也不要覆寫已發布版本。

本次沒有資料遷移；需要回復時以各 repo 對應舊版插件/套件回復，保留使用者資料。新版 fixes 的行為契約與測試名稱詳見各插件本地交接。新 agent 修改前須重新記錄工作樹狀態，不能把本次 baseline 當作未來的 HEAD。

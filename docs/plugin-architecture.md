# 插件平台架構與決策

## 工作區與交接入口

四個插件各有獨立 Git repo、版本、測試與發佈；主程式擁有 SDK 契約、Common 原始碼套件與工作區整合工具。新 agent 先讀當前 repo 的 AGENTS／ARCHITECTURE／HANDOFF，不以其他 repo 的歷史交接推論目前狀態。

目前套件驗證基線為 SDK 1.3.0／AssemblyVersion 1.0.0.0；ADR-004 記錄 2026-09-26 接受的版本更新。ADR-001 保留當時的 1.0.0 決策，不代表目前套件版本。

| 元件 | 改動責任 |
| --- | --- |
| SDK | 跨插件資料／capability 契約，不加入 UI、HTTP、站點或儲存政策 |
| Common / Secrets | internal source-only 原語：限流、持久化、原子寫入、DPAPI、作業結束與 deadline |
| 插件入口 | SDK 適配與依賴組裝；保留目前公開名稱、constructor、能力及 metadata |
| Runtime／coordinator | 資源所有權、請求去重、generation、完整 producer 與 cache publish |
| Client／browser host | HTTP／JSON／WebView2 操作；可替換外部操作的窄接點 |
| Parser／policy／cache wrapper | 各供應商規則、schema、TTL、key、rating、tags；不能泛化進 Common |

## ADR-001：保留獨立插件與二進位契約（2026-09-22；SDK 套件版本由 ADR-004 更新）

SDK 精確版本保持 1.0.0，AssemblyVersion 1.0.0.0；插件排除 SDK runtime asset，由宿主 default AssemblyLoadContext 提供唯一型別身分。Common/Secrets 0.2.0 是 contentFiles Compile 原始碼套件，所有型別 internal，沒有 runtime DLL。Secrets 本次只同步家族版本，DPAPI 實作未改。

不建立通用 plugin base，不讓插件參考宿主／其他插件 project 或 sibling source。Pixiv 的兩個既有 public parser 是相容性承諾，不能為了「只有入口 public」刪掉。

## ADR-002：以完整 producer 擁有生命週期（2026-09-22）

```text
caller A ─ WaitAsync(A token) ─┐
                              ├─ shared producer ─ client → mapping → cache mutation
caller B ─ WaitAsync(B token) ─┘       │
                               runtime shutdown token

Dispose: close admission → cancel → drain within one deadline → begin execution cleanup
                                                               │
                         actual cleanup terminal + producer count zero → persistence dispose
```

`PluginOperationDrain.RunProducerAsync` 在 admission gate 計數；`Completion` 表示 execution 和 persistence 真正到達終態，可能晚於 Dispose 返回。關閉錯誤被記錄／觀察，不以清理例外掩蓋 producer 的原始結果。所有 owner callback 在計數鎖外。

`DisposalDeadline` 使用 TimeProvider 單調時間，一次 10 秒 budget 由所有等待共享。這是同步等待預算，無法搶占 COM、任意 callback、lock 競爭或磁碟 I/O。Fanbox cleanup 必須排到原 dispatcher；逾時不能改在 caller 執行緒 Close/DestroyWindow。不能把「停止等待」當作「資源已釋放」。

供應商特性保留在 local owner：Bepis 使用 cookie session generation/lease；Pixiv 使用 author generation + operation identity + staged-avatar promotion；Fanbox 保存完整 pending PostEntry，三鍵 metadata mutation 只標記一次 dirty。

## ADR-003：可以失敗的架構檢查（2026-09-22）

每個插件的既有 xUnit 專案使用 test-only NetArchTest.Rules 1.3.2，檢查明確型別集合。規則包含公開 API/capability、依賴方向、Common 不依賴 provider、policy/parser 不依賴 transport，並以 ordinary/static/async fixtures 證明能攔錯。禁止空集合假通過。

各 repo 的 Verify-Plugin 驗證 evaluated MSBuild metadata、正式輸出而非測試輸出、文件連結，Build 和 Release 都呼叫它。SDK DLL 在 test output 合法，在 plugin shipping output 不合法。不得以刪除規則／放寬 allowlist 取代相容性證據；有意改變邊界時新增 ADR、回歸測試與遷移說明。

文件和 CI 不能保證任意 agent 都遵守；它們提供可審查規則與可執行的回退防線。遠端 branch protection 未在本任務設定。

## ADR-004：接受 SDK 1.3.0 作為當前套件基線（2026-09-26）

宿主工作樹已有 SDK 1.3.0 的新增契約：`IPluginHost.Language` 與 `ITagDictionaryProvider`／`TagArticle`，供 Pixiv tag 資料使用。使用者決定保留這些既有變更；本次重構的套件、範例與插件驗證均以精確版本 1.3.0 為準。這是對 ADR-001 套件版本決策的更新，未改動其唯一 runtime assembly、compile-only 引用和獨立 repo 邊界。

SDK 的 `AssemblyVersion` 仍為 1.0.0.0，`Language` 有預設實作，新增 tag 能力為可選介面。套件驗證從 fresh local feed 編譯新增 API、檢查 restore graph 的精確版本；範例測試鎖定 public capability 及 assembly identity，各插件驗證須確認相同版本且正式輸出不複製 SDK DLL。這些檢查支援本機相容性評估；遠端 GitHub Packages 和 CI 尚須發布後驗證。

## 新增插件與驗證

從 [MinimalPlugin](../templates/MinimalPlugin/ARCHITECTURE.md) 複製獨立 repo 骨架。它包含假來源、SDK adapter、純 parser、離線測試、CI、驗證腳本及繁中文件，不在主程式 compile glob 或正式發佈清單。HTTP、快取、Common、WebView2 只在需求出現時加入。

單 repo：執行該 repo 的 `eng/Verify-Plugin.ps1`。五 repo 本地整合：在主程式執行 [Verify-PluginWorkspace](../eng/Verify-PluginWorkspace.ps1)。此工具只做 local-feed 打包、各 repo fresh restore/test/產物驗證、範例與正式 loader probe；不改 Git、不部署、不發布。

Loader probe 使用正式 PluginLoadContext，檢查 reflection discovery、capabilities、default-context SDK identity 和無參數建立/Dispose；不代表已驗證登入、API、真實 browser challenge 或 UI。

完整交接與尚未驗證範圍：[plugin-refactor-handoff](plugin-refactor-handoff.md)。

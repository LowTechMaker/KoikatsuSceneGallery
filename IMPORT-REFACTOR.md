# 匯入交易協調抽取

## 第三十一階段：本機真實 sample metadata 驗證

2026-09-13 重新閱讀專案規則與 RTK 規則，確認 Graft 查詢已恢復；目前工作樹
另有 ImportLibraryIndexer、ImportService 等未提交變更，均保留不動。

確認 sample 內有 15 張被 Git 忽略的 PNG 卡片。建立本機 ignored probe，直接
引用正式 Core，分別呼叫 CardMetadataReader 的摘要與完整模式，並產生每檔
SHA-256、卡片類型、名稱是否存在、區塊大小／狀態／診斷報告。卡片唯讀，輸出
只寫入 artifacts/metadata-probe，不把使用者的本機卡片加入 CI 或追蹤檔案。

命令：

`rtk proxy dotnet run --project artifacts/metadata-probe/MetadataProbe.csproj -c Release -- sample artifacts/metadata-probe/report.json`

結果：15 張均可讀且兩種模式的 type／name／plugin GUID list 一致。分類為
11 Koikatu、1 KoikatsuSunshine、3 KoikatuClothes；其中 8 張 KKEx 超過既有
64 MiB 上限，回報 partial／BlockSizeLimit，不能列為完整資料通過。1 張服裝
卡片名稱為空；尚無 KKManager 獨立輸出基準，不自行改變名稱政策。

`rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`

目前 Core 487 通過、0 略過，TRX：core/*2026-09-13_01_13_06_net10.0.trx。
本輪沒有修改正式程式，沒有重新執行 Windows 整合或完整 App 建置；舊數字不
當作本輪新結果。真實卡片與 KKManager 輸出逐欄對照及 UI 驗收仍未完成。

## 第三十階段：載入收尾避開已釋放的 CTS

RunLoadAsync 集中化後檢查到原 finally 使用 `_loadCts?.Token` 判定目前工作；
各圖庫 Dispose 可能已在 await 期間釋放 CTS，這時 getter 本身會拋出
ObjectDisposedException。以本機 .NET 直接執行 CTS.Dispose 後 get_Token 確認
例外，ReferenceEquals 對已釋放的實例仍正常回傳 true。

現在 BeginLoad 後立即擷取 source 實例，finally 比較 ReferenceEquals。
每次 BeginLoad 都建立新的 CTS，因此仍只讓目前那次載入清除 IsLoading，
不再存取已釋放來源的 Token。其餘取消、刷新與 CardsReloaded 順序不變。
Graft 全部 ViewModels 搜尋確認舊 `_loadCts?.Token` 收尾判斷已移除。

2026-09-08 Windows 整合 40 通過、0 略過（12_36_56 TRX），測試命令亦完成
主程式與整合組件 Release x64 建置；本輪未另跑獨立 build，Core 未變更並
沿用第二十八階段 329 通過结果。diff --check 通過。沒有新增只測試框架 getter
的鏡像測試；實際 ViewModel 釋放途中載入完成的 UI 案例仍待人工驗證。

## 第二十九階段：四個圖庫共用載入前後處理

GalleryViewModelBase.RunLoadAsync 集中 BeginLoad、可選 metadata 取消、縮圖狀態
重設、IsLoading、集合刷新 deferral、取消例外記錄與 finally CardsReloaded。
相同 token 才清除 IsLoading 的判斷與原順序保留。四個 LoadCardsAsync 仍是同名
RelayCommand 的 Task 入口，提供原本 try 內的非同步主體及原 cancellation log key。

搬移前 LoadCardsAsync 範圍：GalleryViewModel.cs 135–287、CharacterGalleryViewModel.cs
98–148、CoordinateGalleryViewModel.cs 76–125、MediaGalleryViewModel.cs 45–103。
以搬移前工作樹字串逐段比較，場景／角色／服裝的 try 主體與新委派主體完全相同，
包含設定、索引、快取、版本、篩選及監看順序。媒體主體僅將漏未共用的 TCS／
TryEnqueue／Wait 區塊替換為既有 PublishScannedBatch，卡片 TryAdd／Cards.Add 保留。
媒體因此取得第十七階段既有的拒絕回報與逾時／取消舊回呼失效保護。

本輪不新增模擬 WinUI 的測試或鏡像實作測試；沿用共用發布元件與真實佇列測試，
並做來源對照。AdvancedCollectionView 與四個 ViewModel 的實際刷新／取消／重載
仍屬人工待驗，不能以目前無完整 XAML host 的服務整合結果替代。

2026-09-08 Windows 整合 40 通過、0 略過（12_32_59 TRX），命令沿用第十七階段。
Core 元件未變更，沿用第二十八階段 329 通過結果，未重跑。未推送或觸發遠端 CI。
Windows x64 Release 建置成功，0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

## 第二十八階段：metadata 發布錯誤集中回報

先加入四個失敗案例：dispatcher 回傳 false 無任何紀錄、dispatcher 拋出例外
使 finally 逃出，以及 apply／onCompleted 例外逃出 UI 佇列。修正前 4 失敗、
11 通過，TRX 為 core/*12_26_53_net10.0.trx。

DispatchCurrent 現在接收卡片身分，集中回報排程拒絕、排程例外及排隊回呼的
執行例外；開始前及真正執行時都檢查 scan／worker token。reportError 仍不能
拋出例外，現在可能在 worker 或 owner 執行緒呼叫，契約已明確註記。三個圖庫
使用既有 logger 回呼，不修改記錄 operation 名稱。

保留 metadata 與完成通知的兩次排程、pending 計數順序與非同步工作契約。此修正
只處理排隊回呼；cache-hit 的同步 apply 錯誤仍由直接呼叫端處理。若完成通知
本身被 dispatcher 拒絕，不能在 worker 修改 UI 計數：記錄錯誤後由下一次 Start
或 Cancel(resetCount:true) 重設。沒有假裝已完成 UI 發布，也不將 Begin/Queue
改成等待 UI。四個測試同時確認失敗後可重新啟動並完成。

2026-09-08 完整 Core 329 通過（12_27_35 TRX），完整命令沿用第十七階段。
人工介面與遠端 CI 尚未執行。
Windows 整合 40 通過、0 略過（12_28_33 TRX）；Windows x64 Release 建置成功，
0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

## 第二十七階段：整體驗收證據盤點

新增 ACCEPTANCE.md，直接從最新 Core／Windows TRX 的計數與測試類別核對覆蓋，
整理主要需求、程式邊界、人工步驟與缺少的證據。確認 325／40 為已完成的本機
結果；遠端 workflow 雖已配置 TRX 與 always 保存，仍沒有遠端執行證據。

METADATA.md 的 203 測試與舊掃描生命週期改明確標為歷史紀錄，補上目前的
持久化／取消／關閉行為與驗收指引。CardMetadataTests 的 15 個案例使用合成
卡片，不能當作真實卡片與 KKManager 對照已通過。原本人工清單仍保留未執行。

本輪只有驗收盤點與文件更新，沒有重新測試或建置；引用第二十六階段結果，
diff --check 通過。不宣稱整個專案共用化已完成，下一個可處理缺口為 metadata
發布錯誤的可觀察性；其餘候選列在 COMMON-CODE.md 與驗收矩陣。

## 第二十六階段：快取首次載入共用同步

MetadataCacheService 原 EnsureLoaded 在讀取前先發布 volatile loaded=true，另一個
呼叫可能略過 lock 而讀到尚未填完的 dictionary。場景快取有相似的雙重檢查鎖，
但只在讀取後設定 loaded。兩者現在共用 Core/Helpers/CacheLoadGate，只有回呼
執行完成後才發布完成狀態；load 回傳 false 代表 owner 要求下一次重試。

移除兩個服務各自的 load lock／loaded 欄位，保留讀取、JSON 反序列化、逐筆加入
與例外記錄區塊。metadata 對缺檔、null、已記錄例外均回傳 true，維持原本單次
嘗試政策；場景對缺檔／null 回傳 false，對成功或已記錄例外回傳 true。沒有
將兩種快取的重試政策統一，也未更動快取格式。OperationDrain 仍包住真正載入。

新增三個 Core 測試涵蓋 owner 的重試決定、未處理例外後重試，以及多個讀取者
共用同一次載入；並行測試使用 gate、完成訊號與期限。新增六個服務整合案例，
使用各自唯一的暫存 JSON，覆蓋缺檔、null、損壞 JSON 後補上有效檔案的差異；
metadata 另以全新服務確認同份有效資料與 key 可讀，避免因測試資料錯誤而假通過。
finally 刪除精確暫存檔，不使用正式快取或網路。

2026-09-08 完整命令沿用第十七階段：Core 325 通過（12_19_42 TRX），Windows
整合 40 通過、0 略過（12_20_53 TRX）。人工介面與遠端 CI 尚未執行。
Windows x64 Release 建置成功，0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

## 第二十五階段：metadata 服務沿用操作收尾

MetadataCacheService 與場景快取共用 OperationDrain，不再僅以 ViewModel 掃描
工作作為持久化安全的間接證據。EnsureLoaded、ParseAndCache、Invalidate 都在
服務入口登錄操作；ParseAndCache 原有區塊完整移到 ParseAndCacheCore。正常解析、
key、快取格式與 cancellationToken 檢查順序保留；停止後 ParseAndCache 明確拋出
ObjectDisposedException，Invalidate 略過，TryGetCached 不再載入磁碟但仍可讀取
已保留的記憶體資料。

internal DisposePersistence 先檢查服務操作已 drain，否則拋出明確錯誤，避免
owner 順序錯誤時在解析下方釋放資源。App 的兩個 metadata 參與者同時停止並等待
ViewModel 協調器與服務操作，仍共用原本 ShutdownCoordinator 的五秒總期限。
沒有新增公開關閉入口、設定或資料遷移。

既有忽略取消的整合案例改為在正式服務 Parse 內控制 gate，保留原本最後寫入
斷言，並加入解析進行中拒絕提前 DisposePersistence 的斷言。新增一個案例確認
停止後拒絕解析、忽略失效，以及既有 metadata／最後 JSON 保留。這些測試不
依賴 UI、網路、使用者快取或正式卡片。

2026-09-08 Windows 整合測試 34 通過、0 略過（12_16_12 TRX），命令沿用
第十七階段。Core 元件本輪未變更，沿用第二十四階段 322 通過結果，未重跑。
人工 UI／關閉與遠端 CI 仍未執行。
Windows x64 Release 建置成功，0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

## 第二十四階段：場景快取寫入停止邊界

Core 新增 OperationDrain，TryRun 只在登錄／結束時持鎖，操作本身不在鎖內執行；
StopAsync 永久拒絕新操作並回傳同一個 drain Task，已接受的操作包含例外路徑
都會在 finally 離開。元件不負責等待期限或 I/O，期限仍由 ShutdownCoordinator
統一管理。

SceneCardCacheService 的 EnsureLoaded、UpdateAll、Add、Remove、SetThumbnailPath
都通過同一入口。停止後晚到寫入被略過，LoadAll 仍可讀取已保留的記憶體快照。
已進入的操作會完成後才允許最後 Flush。Dispose 若遇未完成操作，會延後釋放
持久化並觀察工作例外，不同步阻塞 UI。原有 dictionary 更新區塊與大小寫比較
保持不變；正常執行期間未新增掃描／檔案監看／縮圖取消政策。

App 的共用關閉參與者新增場景快取 StopAsync／Dispose，與兩個 metadata 服務
共用原本五秒總期限。這個停止邊界保護持久化資料，不代表背景縮圖 I/O 或整個
圖庫掃描已強制停止。逾時與程序退出的限制沿用第二十二階段；原診斷名稱保留。

新增兩個 Core 測試：停止時拒絕新工作、等待已接受操作，以及例外釋放後可再次
操作但停止後永久拒絕。新增一個正式場景快取整合案例，停止後依序送入整批更新、
監看新增／移除與縮圖更新，確認最後 JSON 與記憶體仍是停止前的內容。
測試使用 gate 與完成訊號，均限時收尾，不操作正式資料。

2026-09-08 Core 322 通過（12_11_07 TRX）。命令沿用第十七階段；人工關閉與
頁面操作仍未驗證，未推送或觸發遠端 CI。
Windows 整合 33 通過、0 略過（12_12_03 TRX）；Windows x64 Release 建置成功，
0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

## 第二十三階段：場景快取沿用共用持久化

SceneCardCacheService 原 91–127 的 ScheduleSave／Flush／Dispose、save lock、Timer
及 dirty 欄位改用同一份 DebouncedDiskPersistence。原 Dictionary 快照保留
OrdinalIgnoreCase，CachedCardEntry 七個欄位、scene_cards.json、EnsureLoaded、
UpdateAll、Add、Remove 與 SetThumbnailPath 的操作順序不變。公開建構／Dispose
入口維持相容，internal 工廠入口支援隔離測試；未修改 PluginCommon 原始碼。

測試新增 ControlledCachePersistence，共用記憶體 writer、失敗切換、序列化後
回呼與清理，metadata 的既有三個測試改用同一輔助元件且保留斷言。新增兩個
場景快取測試涵蓋大小寫路徑存取、縮圖欄位、JSON 資料、Remove、UpdateAll、
Dispose 最後寫入，以及寫入失敗重試與快照期間變更保留 dirty。所有快取路徑
使用不重複的暫存名稱，writer 不開檔；測試手動 Flush，不使用固定 sleep。

責任盤點顯示場景快取生產者包括 LoadCardsAsync 的 UpdateAll、檔案監看 Add／
Remove，以及縮圖完成時的 SetThumbnailPath。其 App 關閉收尾尚未串接；不能只
等待 metadata 就安全 Dispose 此快取，因此本輪沒有把它加入第二十二階段的
metadata 關閉參與者。後續需對所有場景寫入來源建立停止與收尾責任。

2026-09-08 Windows 整合命令沿用第十七階段：32 通過、0 略過（12_07_14 TRX）。
Windows x64 Release 獨立建置成功，0 警告、0 錯誤。
Core／元件未變更，沿用第二十二階段 320 通過結果，未重跑。diff --check 通過，
Graft 已刷新。人工 UI 與遠端 CI 未執行，不使用正式快取或圖庫。

## 第二十二階段：metadata 關閉收尾串接

Core 新增 ShutdownCoordinator／ShutdownParticipant，集中啟動停止、共用等待期限、
冪等工作與停止後才釋放資源。參與者 Stop 必須在回傳 Task 前禁止新工作；等待
逾時不取消或丟棄其工作。各參與者正常 drain 後才在背景呼叫 DisposeResources；
停止失敗只記錄且略過該資源的釋放，其他參與者仍正常完成。此元件預期由 owner
執行緒串行呼叫 Start／WaitAsync，不是任意執行緒安全的公開 API。

角色、服裝 ViewModel 增加 internal StopMetadataAsync，先設定停止旗標並停計時器，
再呼叫共用掃描的 StopAsync。StartMetadataScan／QueueMetadata 在停止後略過，
避免尚未結束的圖庫載入或已排隊監看事件重新啟動解析。快取增加 internal
DisposePersistence，限 owner 確認解析結束後使用；沒有公開 API 或快取格式變更。

AppWindow.Closing 在第一次系統關閉時取消該事件，等兩個參與者共用的五秒期限，
再 Close；重複關閉維持等待同一工作。原 Closed 的縮圖／插件收尾保留，並補上
可能繞過 Closing 的同一 metadata cleanup 嘗試。參考
[AppWindow.Closing 官方文件](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.appwindow.closing?view=windows-app-sdk-2.0)：
Destroy 不觸發 Closing。直接破壞視窗、逾時後關閉、程序被終止均不能保證背景
最後 Flush 完成；逾時記錄 App.MetadataShutdownTimeout，沒有宣稱能強制停止 I/O。

自動測試新增兩個 Core 案例驗證兩個參與者共同期限、重複啟動、逾時後延後釋放
與失敗隔離；新增一個整合案例串接正式 MetadataCacheService、掃描與收尾協調器，
確認忽略取消的 worker 返回後才 Flush 到記憶體 writer。使用完成訊號、可釋放
gate 與期限，不讀寫正式設定或圖庫。Core 320 通過（12_01_12 TRX）；Windows
整合 30 通過、0 略過（12_02_57 TRX），完整命令沿用第十七階段。
Windows x64 Release 獨立建置成功，0 警告、0 錯誤；diff --check 通過，Graft 已刷新。

人工待驗：背景解析時按關閉、重複按關閉、解析正常結束後檢查快取、模擬超時
確認五秒後允許關閉及診斷。目前沒有實際操作視窗，元件／服務測試不替代這些
AppWindow 事件及程序退出驗證。未推送或觸發遠端 CI。

## 第二十一階段：metadata 工作收尾追蹤

檢查 App.xaml.cs 225–229：Closed 只釋放縮圖排程器與插件。MainWindow 未另行
處理 metadata 收尾；CharacterGalleryViewModel.Dispose 只取消協調器，而
MetadataScanCoordinator.Dispose 原本不等待背景解析。不能直接在 Closed 補上
快取 Dispose，就宣稱所有解析結果已儲存。

本輪先在共用 MetadataScanCoordinator.Run 追蹤仍有效或已被取代但尚未完成的
Task；新 Run 清除已完成的追蹤。internal StopAsync 在 owner 執行緒先 Dispose
禁止新工作，再等待全部背景工作；呼叫端 token 只取消等待，不丟棄工作追蹤。
預期的 pipeline 取消作為正常停止處理，其他例外仍傳遞。UI 舊回呼繼續沿用
第十九階段有效性檢查；Start 的任務語意未改。

新增兩個受控工作案例：舊解析忽略取消且已被空的新掃描取代時仍等待其結束；
取消收尾等待後再次 StopAsync 仍能等待同一工作。測試先發出進入訊號，finally
釋放 gate 並限時收尾，沒有固定 sleep。Core 318 通過（11_56_44 TRX）。

這是關閉流程的共用基礎，尚未串接 App 關閉或快取最後 Flush；因此仍不能宣稱
正常關閉已保證存檔。後續必須在允許視窗真正關閉之前禁止新生產者、取消並等待
兩個 metadata pipeline，採用同一個總期限；超時工作最後完成後再釋放持久化，
同時如實區分程序退出可能中止背景工作的限制。不可在 UI 執行緒同步等待需要
UI 發布才能完成的工作，或讓兩個服務各自重新計算完整期限。

Windows 整合命令沿用第十七階段：29 通過、0 略過（11_57_41 TRX），其中已建置
主程式及測試組件的 Release x64 版本；本輪未另跑獨立 build 命令。diff --check
通過，Graft 已刷新。人工關閉驗證與遠端 CI 尚未執行。

## 第二十階段：metadata 快取共用持久化

MetadataCacheService 原 85–115 的 ScheduleSave／Flush 與私有 Timer、dirty、save
lock，改為呼叫既有 PluginCommon/DebouncedDiskPersistence.MarkDirty。主程式明確
編譯 PluginCommon 的 DebouncedDiskPersistence.cs 及 AtomicFileWriter.cs，未修改
這兩份共用來源、Plugin SDK 或任何插件套件。沒有增加另一份持久化實作或 runtime
組件；本次是 host 使用工作樹既有來源，未宣稱完成任何遠端套件發布／獨立還原。

序列化仍使用 Dictionary<string,TMetadata> 快照與原 JSON options，路徑、檔名、
SHA256 key、解析、讀取及失效政策保留。公開建構方式相容；新增 internal 建構
入口注入完整快取路徑與持久化工廠，讓整合測試使用記憶體 writer 並釋放自己的
Timer。正式服务仍維持程序生命週期，未新增公開 Dispose 或變更 App 關閉順序。

沿用共用元件的兩秒 debounce、持有 save lock 的原子寫入、generation 比對及
失敗後五秒至五分鐘退避。新增兩個正式 MetadataCacheService 衍生測試，驗證失敗
後 dirty／retry、成功清除／reset、Invalidate 寫出移除，以及序列化後發生新變更
仍保持 dirty 到下一份快照。測試不使用使用者快取或正式圖庫，手動觸發 Flush，
不以固定 sleep 等待 Timer。這兩個案例不代表已驗證程序關閉的最後 flush。

2026-09-08 完整命令沿用第十七階段：Core 316 通過（11_52_51 TRX），Windows
整合 29 通過、0 略過（11_52_21 TRX）；Windows x64 Release 建置成功，0 警告、
0 錯誤。diff --check 通過，Graft 已刷新。人工 UI 與遠端 CI 仍未執行。

## 第十九階段：共用 metadata 回呼有效性

盤點結果整理於 COMMON-CODE.md，列出已共用責任、必須保留的圖庫差異，以及
載入工作階段、metadata 持久化和目的地規劃的後續候選。

MetadataScanCoordinator.ParseOneAsync 原本只有完成回呼檢查 scanToken 與 worker
token；metadata apply 回呼排隊後不再檢查。新增三個受控佇列測試先確認失敗：
取消與 Dispose 後仍把 Loaded 設為 true；同一卡片新掃描值 2 被晚到舊值 1 覆蓋。
失敗 TRX 為 core/*2026-09-08_11_46_40_net10.0.trx（3 失敗、6 通過）。

現在兩種回呼都經由 DispatchCurrent，在 UI 實際執行時檢查相同的兩個 token。
原完成回呼的 pending 計數與 onCompleted 順序未改；Start／Queue、快取命中、
解析並行度與錯誤記錄政策不變。未把 Scan 的任務語意改為等待所有 UI 發布，
也未處理已開始執行的回呼中止或卡片單獨移除的政策。

2026-09-08 本機驗證（完整命令沿用第十七階段）：Core 316 通過，Windows 整合
27 通過、0 略過；Windows x64 Release 建置成功，0 警告、0 錯誤；diff --check
通過。成功 TRX 分別為 core/*11_47_09_net10.0.trx 與 integration/*11_47_50_net10.0.trx。
Graft 已刷新；仍未執行人工介面驗收與遠端 CI，不使用正式圖庫。

## 第十八階段：批次發布的真實佇列驗證與共用邊界

新增 GalleryBatchPublicationTests.RealDispatcherPublishesOnUiAndRecoversAfterPublisherFailure，
在真正的專用 DispatcherQueue 上以 Low 優先序執行 AwaitedUiPublication.InvokeBlocking。
驗證發布位於 UI 執行緒、背景等待完成後能看到整批資料、原例外回到等待者，以及
同一佇列在失敗後仍能發布下一批。所有等待有期限，finally 先取消並收尾生產者，
再關閉專用佇列；不開視窗、不建立 WinUI 替身、不使用固定 sleep。

本輪曾嘗試以最小衍生類直接呼叫 GalleryViewModelBase.PublishScannedBatch，搭配
真實 AdvancedCollectionView 驗證延後刷新。建構時 GetForCurrentThread 需要在 UI
執行緒套用 DispatcherActivation；處理後，集合刷新仍在
Microsoft.UI.Xaml.Data.CurrentChangingEventArgs.CreateInstance 發生 COMException。
可重現步驟：專用佇列上建立正式基底 ViewModel，背景呼叫 PublishScannedBatch 加入
兩張 CharacterCard，DeferRefresh.Dispose 觸發上述 XAML 啟用失敗。此 host 尚未
具備完整 XAML 初始化，不能据此判定產品回歸，也不能將目前 27 個通過宣稱為
AdvancedCollectionView 刷新／排序通過。探索用測試已改為上述明確的發布元件範圍，
完整基底與三個圖庫畫面驗收仍待具備 XAML 測試 host 或人工操作。

另比對角色及服裝 BaseFilterPasses／ApplyFilter：角色保留 IsLatestVersion 與
來源篩選，服裝無條件時使用 null 篩選器。文字及 metadata 查詢已共用
CardMetadataQuery／MetadataFilterState，本輪不為合併外觀相似程式而改變這些政策。

2026-09-08 執行第十七階段相同 Windows 整合命令：27 通過、0 略過，主程式與
測試組件 Release x64 建置成功；TRX：integration/*2026-09-08_11_43_55_net10.0.trx。
本輪僅新增測試與紀錄，Core 313 通過及獨立 Release 建置沿用第十七階段結果，
未重跑亦未宣稱新增一次結果。git diff --check 通過；Graft 已刷新。無遠端 CI。

## 第十七階段：圖庫掃描批次共用 UI 發布

場景、角色與服裝圖庫的背景掃描回呼，改由 GalleryViewModelBase.PublishScannedBatch
共用低優先 UI 排程、CardsView.DeferRefresh 與十秒等待上限。Core 的
AwaitedUiPublication.InvokeBlocking 重用既有 InvokeAsync，僅供同步背景生產者使用，
不得在目的地 UI 執行緒呼叫。CardScanService.cs 52–99 的批次入口由 Task.Run／
Parallel.ForEach 呼叫，符合此限制；掃描及各圖庫加入卡片的規則維持原位。

搬移前批次區塊位於 GalleryViewModel.cs 約 217–254、CharacterGalleryViewModel.cs
約 121、CoordinateGalleryViewModel.cs 約 98。原 TCS、TryEnqueue 及等待改為上述
共用入口；場景的 scannedIndex、日期比較、移除舊實例與 R18 判斷保留，角色／服裝
的 TryAdd 與 Cards.Add 順序保留。未修改掃描範圍、快取、metadata 或檔案監看政策。

排程拒絕現在立即以 InvalidOperationException 結束；發布例外回傳背景等待者。
逾時或取消後，尚未開始的回呼失效；已開始執行的回呼不能強制中止。離開等待時
釋放取消來源，並觀察可能晚到的工作例外。此生命週期修正避免逾時後的佇列回呼
繼續加入卡片，但不宣稱取消能回復已套用的一批內容。

2026-09-08 本機自動驗證：

- `dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：313 通過。新增 5 個測試涵蓋等待實際發布、逾時失效、取消失效、拒絕後重試及發布例外傳遞；使用受控回呼，沒有固定 sleep。
- `dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：26 通過、0 略過。
- `dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`：成功，0 警告、0 錯誤。
- `git -c core.safecrlf=false diff --check`：通過。TRX 沿用既有保存位置，Graft MCP 已刷新來源索引。

人工待驗：各圖庫載入超過一批的暫存資料，確認逐批出現、篩選／排序及取消後重載；
上述元件及服務整合測試不等同三個 ViewModel 的畫面驗收。未操作正式圖庫，未推送，
未觸發遠端 CI。

## 第十六階段：補齊真實 DispatcherQueue 測試啟用

第十五階段留下的 Skip 已移除。主程式建置所產生的 WindowsAppSDK.manifest
含有 CoreMessagingXP.dll 的 DispatcherQueue 類別宣告；VSTest 不使用主程式的
內嵌 manifest，因此僅初始化 Windows App SDK 仍無法啟用該類別。

整合測試新增 DispatcherQueue.manifest，依目前 SDK 產生的 manifest 保留三個
Dispatching 類別對應，並由 DispatcherActivation.Run 在同步呼叫範圍中使用
CreateActCtx／ActivateActCtx。啟用內容僅作用於呼叫執行緒，不跨 await；finally
依序解除並釋放，原生元件從測試輸出目錄取得。沒有機器層級註冊、視窗、網路或
產品啟動設定變更。SDK 升級若改變對應，此真實測試會直接失敗，應重新比對 manifest。

參考 [ACTCTX 文件](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-actctxw)
與 [ActivateActCtx 文件](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-activateactctx)。

本機 Windows 整合 26 個通過、0 略過，包含真正的 750ms Tick、重複 Start 不重複
訂閱、Complete 最後刷新、Dispose 後不再啟動或刷新。測試等待完成訊號且有逾時，
finally 關閉專用 DispatcherQueue；跨執行緒的例外轉回測試結果。TRX 沿用第十階段路徑。
此結果取代第十五階段的計時器待驗狀態，但不代表三個圖庫的畫面、繫結或導航已
完成人工驗收。遠端 CI 尚未執行。

## 第十五階段：圖庫 metadata 更新計時器

場景、角色、服裝圖庫共用 Services/GalleryMetadataRefresh，管理延遲建立的
DispatcherQueueTimer、750ms 重複更新、完成時停止並最後刷新，以及釋放。
三個 ViewModel 保留各自 RefreshMetadataFilter：場景只有 HasMetadataFilter
時刷新，角色與服裝維持無條件刷新；沒有引入額外 ViewRefreshed 通知。

搬移前的 Start／Tick／Complete 範圍：GalleryViewModel.cs 328–355、
CharacterGalleryViewModel.cs 235–256、CoordinateGalleryViewModel.cs 154–175。
共用 Start 保留 CreateTimer、Interval、IsRepeating、先解除再加入 Tick 與 Start
的原順序；Complete 仍先 Stop 再執行原刷新區塊。三個取消入口與場景關閉插件分析
入口改呼叫 Stop，Dispose 改解除計時器事件並阻止釋放後再次開始或刷新。

Core／元件測試 308 個通過。真實 DispatcherQueue 計時器整合案例已建立，但本機
VSTest 在 CreateOnDedicatedThread 即出現 CLASS_E_CLASSNOTAVAILABLE，尚未進入
共用計時器；明確執行主組件初始化、改由測試 DLL 初始化皆無法排除。初始化嘗試已
還原，案例標記 Skip，不能作為 runtime 通過證據。先保留此案例，待具備 WinRT
啟用能力的測試 host 再啟用；也可依人工清單確認三個圖庫的背景解析刷新、取消與
離開頁面。未建立假的 WinUI 類別，也未以固定 sleep 假裝已觀察到回呼。

測試 host 初始化調查參考 [Windows App SDK 自包含部署文件](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)。
本輪來源對照與建置只證明抽取相容，不取代計時器 runtime 或介面操作驗證。
Graft MCP 已刷新來源索引，远端 CI 未執行。

本輪最終結果：Core／元件 308 通過；Windows 整合 25 通過、1 Skip（上述真實計時器）；
Windows x64 Release 建置成功，0 警告、0 錯誤；git diff --check 通過。

## 第十四階段：主項目集合沿用共用訂閱

ImportViewModel.Items 改用 CollectionItemObserver<ImportItem>；原 _subscribedItems
以及 Add／Reset 中的手動訂閱程式移除。共用元件增加保留原集合事件參數的回呼入口，
在訂閱同步後才呼叫既有 OnItemsCollectionChanged，因此原 Add 分類、Reset 清空與
統計更新順序保留。原有無參數回呼的兩個群組使用者不需改寫。

這也補齊原 Items.Remove 未解除事件的缺口，已移除項目的晚到 Status／DestinationPath
通知不再透過 ViewModel 觸發統計或 Ready 重新解析。既有 OnItemPropertyChanged
中的 Ready 解析安排完整保留，不修改分類、交易或手動移除容器政策。

新增兩個正式 ImportItem 模型案例：移除回呼內及移除後的通知均被解除；加入回呼前
已訂閱，Clear 後重新加入仍正確收到通知並轉送原事件參數。Core／元件 308 個通過。
測試與 TRX 命令沿用第十階段；元件測試不代表 ViewModel UI 操作驗收，人工清單與
遠端 CI 仍待執行。Graft MCP 已刷新本輪來源索引。

Windows 服務整合 25 個通過；Windows x64 Release 建置成功，0 警告、0 錯誤；
git diff --check 通過。

## 第十三階段：共用群組項目訂閱管理

新增 Core 的 CollectionItemObserver<T>，用實例身分追蹤集合項目的 PropertyChanged。
FetchFailedGroups 與 UnknownGroups 共用此元件，集合變更時先同步訂閱，再執行
ViewModel 原有 HasItems 與選取數更新。所有操作由 UI 執行緒串行執行，不新增排程。

原 ImportViewModel.cs 314–342、353–381 的兩套訂閱管理移至元件 Reconcile；
原 454–459 的額外 Reset 解除訂閱移除，因兩個群組集合 Clear 已觸發共用元件。
新 ViewModel 的 OnFetchFailedGroupsChanged（314–318）與 OnUnknownGroupsChanged
（329–333）只保留原有呈現狀態及選取數更新。分類、手動指派、命令與 Items 訂閱不變。

檢查原程式發現 Replace 只解除舊項目卻未訂閱新項目；共用元件依目前集合同步，
修正替換後無法追蹤選取變更的缺口。相同實例重複出現只訂閱一次，移除最後一個
出現位置才解除；不同實例即使值相等也各自訂閱。元件提供冪等 Dispose 解除集合與
項目訂閱；目前兩個元件與其 ViewModel 同生命週期，清空工作區時繼續保留集合監聽。

本機驗證（2026-09-08）：Core／元件 306 個通過，包含新增 5 個 Replace、
Add／Remove／Clear、重複實例、Move 與 Dispose 案例；Windows 服務整合 25 個通過。
命令與 TRX 路徑沿用第十階段。服務整合測試不覆蓋 ViewModel 的實際 UI 繫結，
群組選取與介面操作仍依第十階段清單標示待驗。未推送或觸發遠端 CI。
Graft MCP 已刷新來源索引；環境仍無 graft CLI。

Windows x64 Release 建置成功，0 警告、0 錯誤；git diff --check 通過。

## 第十二階段：共用作品資料夾搜尋

Core 新增內部 ArtworkDirectoryLookup.FindFirst（7–20），只接收候選路徑、
目標 provider／作品 ID 及回傳可空值組的名稱解析委派。依輸入順序以
OrdinalIgnoreCase 比對兩個 ID，第一筆匹配即停止列舉並釋放列舉器。
null 表示沒有匹配；解析與列舉例外（包含取消）原樣傳回呼叫端。
沒有新增 Plugin SDK 相依、檔案系統框架、快取或排程。

### 搬移對照與政策

| 原位置（第十一階段完成時） | 新位置與保留規則 |
| --- | --- |
| ImportService.cs 937–958 FindArtworkDirectory 的 FirstOrDefault 身分比對 | 呼叫 ArtworkDirectoryLookup.FindFirst；原目錄存在／provider 檢查、metadata 排除及路徑 OrdinalIgnoreCase 排序保留 |
| AuthorPostService.cs 579–596 FindArtworkDirectory 的列舉與身分比對 | 呼叫同一方法；原始列舉顺序保留，不新增 metadata 排除或缺失目錄容錯；方法改 internal 供直接整合測試 |

FindRootArtworkFiles、作者作品掃描、指派搬移、目的地命名、metadata 寫入與取消
呼叫點沒有搬移或修改。兩個服務不共用排序政策，也不假設同作品多目錄時選擇相同。

### 驗證（本機 Windows，2026-09-08）

- Core／元件：301 個通過，包含新增 9 個查詢案例，涵蓋空候選、null／不匹配、大小寫、多筆匹配依序選擇、提早停止與列舉器釋放、解析／取消／列舉例外。
- Windows 新增 4 個案例：匯入端排序與 metadata 排除（含只有 metadata 的情況）、作者端實際列舉順序及不排除 metadata、無匹配與缺失目錄例外。只使用暫存目錄、離線 provider 與查詢入口，不呼叫搬移流程。
- Windows 整合共 25 個通過，原有 21 個斷言保留。Windows x64 Release 建置成功，0 警告、0 錯誤；git diff --check 通過。
- 執行命令沿用第十階段的 Core／Windows 整合測試及 Windows x64 Release 建置命令；TRX 保存於 artifacts/test-results，CI 保存設定不變。
- Graft MCP 已刷新來源索引；環境仍無 graft CLI。人工介面驗證與遠端 CI 尚未執行，沿用第十階段待驗清單，未推送或觸發 workflow。

## 第十一階段：匯入圖庫索引

新增 Core 內部 ImportLibraryIndexer，集中檔名索引、逐位元組重複比對及作者資料夾
索引。注入同一個 LibraryFileCache 與錯誤記錄委派（IAppLogger 位於主專案，故不由
Core 直接引用）。Core 不增加 WinUI、Plugin SDK、ImportItem 或 SettingsService 相依。
request 僅包含來源路徑／名稱、根目錄與資料夾設定的獨立快照，以及作者 ID 解析委派。
解析委派在背景執行，null 表示不辨識；取消例外向外傳遞，其餘錯誤沿用原記錄邊界。

ImportService 在既有 UI 快照回呼中同時擷取項目實例與來源值；前者只用於結果套用
時排除已移除項目，後者提供背景索引，不從背景重讀 ImportItem。設定載入後轉換為
request，結果仍由原解析協調器發布。快取登錄、失效、GetKnownAuthorsAsync 的舊式
作者搜尋及作品資料夾查詢保留原位；每個服務與索引器共用同一份快取。

### 搬移對照

以下舊位置以第十階段完成時的 Services/ImportService.cs 為準，新位置位於
KoikatsuSceneGallery.Core/Services/ImportLibraryIndexer.cs：

| 原始位置 | 新位置／調整 |
| --- | --- |
| 1036–1067 BuildLibraryIndexAsync | 37–66 BuildAsync，輸入改用 request，診斷計時與計數保留 |
| 1076–1105 BuildExistingFileIndex | 68–97，同樣以 *.png 遞迴列舉並保留根目錄順序 |
| 1107–1163 FindIdenticalSourcePaths | 99–155，ImportItem 改為 ImportLibrarySource，內容比較與計數流程相同 |
| 1165–1230 BuildFolderIndex | 157–205，設定／provider 查詢改用快照與解析委派，保留 TryAdd 首筆優先 |
| 930–944 結果模型 | 20–28 ImportLibraryIndexResult、207–211 DuplicateDetectionResult |
| 1241–1246 BuildScopeKey | 213–218 共用建構方法，服務所有 scope 查詢轉接到此方法 |
| 1069–1074 根目錄整理及 provider 設定轉換 | 服務 1027–1041 CreateLibraryIndexRequest，保持 OrdinalIgnoreCase 去重及原 provider 順序 |

逐段對照確認 GetKnownAuthorsAsync、ResolveArtworkDestinations 未變動；
ResolveDestinations 僅替換 scope key 方法的限定名稱，目的地演算法未修改。
不新增排序、作者選擇規則、快取格式、命名規則或檔案系統框架。

### 驗證（本機 Windows，2026-09-08）

- 自動測試通過：Core／元件 292 個（原 285 加新增 7 個），命令沿用第十階段含 TRX 的 Core 測試命令。
- 自動測試通過：原有 21 個 Windows 服務整合案例，未修改任何原斷言，命令沿用第十階段含 TRX 的整合測試命令。
- Windows x64 Release 建置通過，0 警告、0 錯誤；git diff --check 通過，命令沿用第十階段紀錄。
- 新案例涵蓋多根與缺失目錄、同名同長度不同內容、相同內容、診斷計數與 PNG 範圍；作者 provider／遊戲／分級範圍、無分級與空遊戲目錄、無法解析及重複作者；快取重用、登錄、失效與根目錄變更；預先取消、解析取消、解析錯誤保留部分結果及後續範圍繼續。
- 首筆作者資料夾斷言根據實際解析呼叫順序，不假設作業系統的列舉順序。所有資料均為暫存檔案，不使用正式圖庫或網路。
- Linux 遠端 CI、實際介面驗證：尚未執行；沿用第十階段人工操作清單。沒有推送或觸發遠端 workflow，既有 TRX 保存設定未變動。
- Graft MCP 已刷新來源索引；環境沒有 graft CLI，未產生可提交的 manifest。

## 第十階段：匯入驗收與失敗復原

本階段只修改整合測試、CI 與驗收紀錄，沒有新增產品 API 或改動目的地政策。
Queue 以 Publication 的具名步驟集中單一作品分析的發布順序，提供 HoldAnalysisAt
及 RejectAnalysisAt；多批交錯則以 provider／設定完成訊號控制，不假設全域發布順序。
Fixture 改成非同步清理：取消分析與解析、解除受控等待、觀察所有服務呼叫結果，
再以同一協調器的空集合解析確認背景工作已退出，最後才刪除自己建立的暫存目錄。
整個清理共用 20 秒期限；若無法收尾則測試失敗且不提前刪除圖庫。佇列讀取逾時
會取消真正的讀取，避免遺留讀取者吞掉後續回呼。

### 自動驗收矩陣（本機 Windows，2026-09-08）

| 驗收項目 | 測試 | 狀態 |
| --- | --- | --- |
| metadata、Ready、快照及目的地排程拒絕，未發布欄位不變，同服務下一批成功 | RejectedPublicationFailsWithoutApplyingAndNextBatchSucceeds（4 案例） | 自動測試通過 |
| 設定載入錯誤傳給所有等待者，修復後重試成功 | SettingsFailureReachesAllWaitersAndResolutionCanRetry | 自動測試通過 |
| 已被取代的解析晚到錯誤不影響最新結果 | SupersededSettingsFailureDoesNotFailLatestWaiters | 自動測試通過 |
| 清空後舊 provider 晚回覆不污染新批；只取消一批不影響另一批 | CancelPendingProviderDoesNotAffectAnotherBatch（2 案例） | 自動測試通過 |
| 待發布時只移除一項，其 metadata／目的地不再變動，其餘正常完成 | RemovedItemIsSkippedWithoutClearingOtherItems（2 案例） | 自動測試通過 |
| 第九階段分類、內容比對、路徑、交錯與等待案例 | 原有 11 個服務整合案例 | 自動測試通過 |
| Core／元件回歸 | 285 個案例 | 自動測試通過 |

執行命令：

```powershell
dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core
dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration
dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64
git -c core.safecrlf=false diff --check
```

測試結果：Core／元件 285 通過；服務整合 21 通過，包含本階段新增 10 個案例。
Windows x64 Release 建置成功，0 警告、0 錯誤；差異檢查通過。
TRX 存於上述 artifacts/test-results 目錄。測試未揭露需要修改產品程式的回歸。
Windows 與 Linux CI 都輸出 TRX，使用 always() 保存既有結果；Linux 仍只執行
Core／元件測試。建置提前失敗、尚無 TRX 時以警告呈現，不偽造測試結果。
上傳設定依 [upload-artifact 官方文件](https://github.com/actions/upload-artifact) 使用 v7。
遠端 CI 與結果上傳：尚未執行；本階段未推送或觸發 workflow。

### 人工介面驗收清單

全部尚未執行：此環境無原生 Windows UI 操作能力。後續操作應使用隔離測試帳號／
設定及暫存圖庫，準備兩批測試卡、非卡片 PNG 與可手動指派的項目；不按確認匯入，
不使用正式圖庫或網路查詢。每項完成時記錄版本、操作資料與實際結果，才改標人工通過。

| 項目 | 操作 | 預期結果 | 狀態 |
| --- | --- | --- | --- |
| 連續兩批 | 第一批仍分析時加入第二批，包含非卡片 PNG | 有效項目完整保留、拒絕數正確；完成後載入提示消失，按鈕狀態與可匯入項目一致 | 尚未執行 |
| 篩選與選取數 | 建立不同評級項目，切換篩選、全選、取消單項，再重套篩選 | 可見數與選取數正確，全選呈三態；切換或命令重套篩選清除選取，不可選項不被選取 | 尚未執行 |
| 群組導航 | 準備未辨識與已辨識項目，點擊各可用群組導航 | 捲至對應群組，導航數量與版面一致，空群組不產生錯誤 | 尚未執行 |
| 快速切換解析選項 | 同作品多項資料快速調整資料夾門檻及視覺分組，停在確定值 | 最終目的地符合最後選項，沒有舊結果覆蓋或永久忙碌 | 尚未執行 |
| 手動編輯後解析 | 選取項目，套用手動作者／評級，立即調整解析選項 | 顯示與目的地保留手動編輯，解析完成後選取及命令狀態正常 | 尚未執行 |
| 清空再加入 | 分析期間清空，立即加入另一批 | 舊項目與 metadata 不回流，新批正確顯示與完成；清空後摘要、篩選呈現與按鈕一致 | 尚未執行 |

第九階段的最小卡片標頭與離線 provider 限制仍適用；服務整合通過不表示
XAML 繫結、實際按鈕與導航已人工驗收。Graft MCP 已刷新來源索引，環境仍無 graft CLI。

## 第九階段：ImportService 整合測試

新增 KoikatsuSceneGallery.IntegrationTests，直接引用正式主專案，目標為
net10.0-windows10.0.26100.0／win-x64。測試及引用的主專案使用自包含
Windows App SDK；初始化由主專案負責，測試不啟動視窗、不建立 WinUI 替身。
主專案排除測試目錄，主程式與 Core 僅增加指定整合測試組件的 internal 存取權。

ImportService 原公開建構方式保留，轉接到注入設定載入委派、PostMetadataStore
與 LibraryFileCache 的內部建構入口。原 AnalyzeAsync 與
ReResolveWithDetailedDiagnosticsAsync 的 DispatcherQueue 入口，轉接到接收
Func<Action, bool> 的同一份實作。分類、索引、內容比較、目的地及分級政策未搬移或修改。

11 個服務整合案例涵蓋接受／拒絕 PNG、metadata／Ready／目的地發布必須被等待、
兩批查詢反序完成、分類後／metadata／目的地待發布時取消清空，以及新批完成後
執行舊回呼。同時涵蓋重疊解析取得最新選項、單一等待者取消、同名同長度不同內容、
相同內容已在圖庫、沿用既有作者／作品資料夾、provider 失敗與排程拒絕。
所有協調等待均有逾時上限，使用可控制佇列與完成訊號，不用固定 sleep 決定順序。

測試資料由有效 PNG 加上分類器辨識的最小卡片標頭組成；這不是完整可載入遊戲的
角色卡，也不驗證遊戲內 metadata 解析。作品 metadata 由離線 provider 提供。
設定與快取皆為測試實例，圖庫位於每案例獨立的暫存目錄，沒有正式圖庫、
使用者設定、網路或實際匯入搬移。未發現需要修改產品政策的回歸；門檻案例遵循
既有「項目數大於門檻」規則。

本機驗證（2026-09-08）：

- Core／元件測試：285 個通過（dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release）。
- Windows 服務整合：11 個通過（dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true）。
- Windows x64 Release 建置成功，0 警告、0 錯誤（dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64）。
- git diff --check 通過。Graft MCP 已刷新變動來源索引；環境沒有 graft CLI，未產生可提交的 graft/manifest.json。
- Windows CI 已加入同一整合測試命令；Linux 維持 Core／元件測試。遠端 CI 尚未執行。

人工介面驗證仍待執行：按鈕啟用、XAML 繫結、群組導航、連續加入兩批、
快速切換解析選項、手動編輯後立即解析及清空再加入。服務整合測試不取代這些驗證。

## 第八階段：解析協調與可等待發布

修改前 ImportService.ReResolveWithDetailedDiagnosticsAsync（422–484）的背景索引、
UI 套用及診斷計算，改由 ImportResolutionCoordinator 管理等待者与工作生命週期。
AnalyzeAsync（227–332）的三個集合／metadata／Ready 發布點改用 AwaitedUiPublication，
先等待發布完成再進入下一步。初次目的地解析亦進入相同管線，指紋後再次解析保留。
ViewModel 的 DebouncedReResolveAsync（1280–1290）不再自建 CTS，
改傳 debounce 旗標；Clear 在集合清空前取消分析與整輪解析。

ResolveDestinations 與 ResolveArtworkDestinations 的演算法未修改。
套用集合限定為索引快照中仍存在於工作區的實例，避免以舊索引處理新增項目。
metadata／Ready 回呼也會排除已移除項目，批次取消則略過尚未執行的發布。
立即請求優先性維持到本輪發布；正常取代不失敗，等待者接收最新成功結果。
呼叫端取消只取消自身等待，Clear 才取消整輪；不可取消背景工作結束前不啟動下一個。

離線測試使用可控制背景工作、延遲與 UI 佇列，涵蓋合併、優先性、舊結果／錯誤、
單一等待者取消、清空後重啟、發布重入、拒絕排程及例外，並驗證發布必須被等待。
UI 取消註冊及每次解析 CTS 以 finally／using 釋放。
完整 ImportService 與 WinUI 初次分析整合仍待人工操作：連續兩批、
切換選項、手動編輯與清空再加入。離線排程測試不是實際介面操作證據。

## 第七階段：平面審核批次編輯

修改前 ImportViewModel.ApplyReviewChangesAsync（1434–1555）中的驗證、
作品解析／查詢及欄位覆寫移至 ImportReviewEditService.Validate、
PrepareAsync 與 PreparedImportReviewEdit.Apply。原四個命令仍共用
ViewModel 的入口，原資源鍵、忙碌狀態與 finally 保留。

輸入以不可變 request 在操作開始時擷取；查詢後不重新讀取 UI 輸入。
准备成功才 CaptureUndo，作品無法解析／無結果或作者解析例外不會覆蓋
既有復原紀錄。套用順序維持作品 metadata、手動作者、手動評級。
重新分類判斷由 Apply 回傳，容器移除、Ready 狀態與加入群組仍由 VM
按原順序執行。作者／評級覆寫不修改作品快取。移除原本無法抵達的
null metadata 清除分支；正常查詢無結果仍整次不套用。

新增 21 個離線案例涵蓋輸入驗證、評級索引、失敗不變更、覆寫組合、
復原、單次查詢、輸入快照、token／例外與快取保留。
尚待人工驗證四個命令、提示、輸入清空及復原；測試與建置不取代 WinUI 操作。

## 第六階段：反向搜圖結果套用

修改前 ImportViewModel.cs 的 ApplySauceNaoResultToFetchFailedGroupAsync
（707–743）、ApplySauceNaoResultToUnknownGroupAsync（745–784）與
ApplySearchResultToSelectionAsync（1378–1394）之欄位更新，抽至
ImportSearchResultAssignment.Apply，透過三個明確模式保留既有差異。
未知群組僅在有作品 ID 時更新 ArtworkId／ManualArtworkId；擷取失敗群組
僅更新非空 ArtworkId，保留 ManualArtworkId；平面審核兩者均直接覆寫，
包含 null。各模式的屬性賦值順序保留。

快照、項目移出／移入容器、作者選項更新及目的地解析仍由原入口負責。
本次不修改 FetchedArtworkInfo 的既有保留行為，也不產生新的作品 metadata；
完整作品查詢的快取修正限於第五階段。新增六個模式／作品有無的離線案例，
涵蓋欄位更新、快取保留及復原。尚待人工驗證三個入口的搜尋結果套用。

## 第五階段：共用作品指派與 metadata 快取

修改前 ImportViewModel.cs 的 AssignArtworkIdToUnknownGroupAsync（856–898）
及 AssignBatchArtworkIdToUnknownAsync（934–981）中的準備、查詢與結果更新，
改呼叫 ViewModels/ImportArtworkAssignment.cs。輸入驗證、作品 ID 解析、
CaptureUndo、群組移除、批次輸入清空與目的地解析仍留在原入口，順序不變。

明確的行為修正：準備階段清除 FetchedArtworkInfo，成功查詢保存新 ArtworkInfo，
null 結果維持空快取，避免匯入計畫沿用舊 sidecar metadata。
其餘欄位更新及逐項加入 AnalyzingItems 的順序保持原樣。
共用流程保留 UI 同步內容；查詢委派取消／例外向外傳遞，不把未完成工作標為 Ready。
一般服務查詢錯誤仍由 ImportService 記錄後回傳 null。

新增 6 個離線案例涵蓋單次查詢、準備回呼、資料映射、null 保留評級、
取消與例外，以及成功／無結果後復原舊快取。尚待人工確認兩個入口的
成功、失敗與復原操作；本次不修改作品 ID 解析、平面審核或反向搜圖。

## 第四階段：共用作者指派

修改前 ImportViewModel.cs：AssignAuthorToUnknownGroupAsync（835–860）、
AssignBatchAuthorIdToUnknownAsync（909–941）、AssignAuthorAsync（1197–1222）、
AssignBatchAuthorIdToFetchFailedAsync（1227–1263）中的作者欄位更新迴圈，
改呼叫 ViewModels/ImportAuthorAssignment.cs 的 Apply。
未知群組、擷取失敗單組、擷取失敗批次使用三個明確模式：
未知群組不補作者來源；後兩者僅在來源為 null 時補各項目的作品來源。
只有擷取失敗單組保留原 ManualAuthorId；其餘寫入呼叫端已 Trim 的 ID。

驗證、作者解析、CaptureUndo、群組搬移、輸入清除及目的地解析的順序不變。
ImportManualAssignmentHistory 保留原實作，透過來源連結增加 baseline、
操作前快照、單次復原與 Clear 的離線測試。
尚待人工確認四個入口的指派及復原操作；不修改其他手動編輯流程。

## 第三階段：審核工作區

修改前 ImportViewModel.cs 的 1438–1472 行（篩選／全選）、1509–1552 行
（Reconcile／Clear）、1577–1659 行（選取更新／分組／寬度／群組選取）
移至 ViewModels/ImportReviewWorkspace.cs。RefreshReviewGroups 的版面建構、
key 比較、列重用／移動及文字格式維持原有演算法；僅集合、文字解析
及完成通知改由工作區提供。

工作區擁有 States、VisibleItems、Rows 及選取狀態。ViewModel 保留原公開
屬性名稱、命令與 UI 啟用判斷，AdvancedCollectionView 以 VisibleItems
為來源，不再套用第二套篩選。PropertyChanged 轉送包含選取提示、
篩選索引、摘要與群組導航；雙向繫結的相同值回寫不重新修改選取。
SetReviewFilter 命令仍可重套相同篩選以清除選取。

ImportItemReviewState 改由建構參數提供文字解析器，工作區及審核模型
以來源連結編入測試專案，使用實際 CommunityToolkit.Mvvm 及 Plugin SDK，
不需要 WinUI 替身。排程以世代防止 Clear／Dispose 後舊內容回流。

新增 13 個離線案例涵蓋實例重用、解除訂閱、評級篩選、同篩選重套、
可選範圍與三態、狀態變更、列／群組選取、列移動與重用、寬度變更、
排程合併及失效、釋放與文字注入。既有版面與政策測試保留原斷言。
尚待 WinUI 人工確認篩選、選取數、群組導航與批次操作；建置與來源
对照並不取代實際操作驗證。

## 第二階段：分析批次狀態

修改前 ImportViewModel.cs 的 1419–1497 行（Begin/EndAnalysisOperation、
Begin/EndAnalysisProgress、AddRejectedAnalysisCount、UpdateAnalysisProgress）
移至 Core/Services/ImportAnalysisTracker.cs。ViewModel 的 UpdateAnalysisProgress
僅提供已完成路徑並套用快照；AddFilesAsync 的分析步驟順序維持不變。
Clear 改呼叫 CancelAll，集合 Reset 改呼叫 ResetProgress。

每批 handle 擁有 CTS，finally Dispose 完成清理；取消不代表工作已完成。
Reset 只切換進度世代，舊批次仍可取消，但不再影響新世代計數。
唯一分析路徑及完成路徑以 OrdinalIgnoreCase 比較。
Tracker 所有狀態操作由 UI 執行緒串行呼叫。

新增離線測試涵蓋交錯批次、重複路徑／結束、拒絕數上下限、
取消與清理、重設後舊批次完成，以及例外清理後再次分析。
尚待人工驗證連續拖入兩批、無效 PNG、進度及按鈕狀態。
本階段未修改 ImportService 已排入 UI 佇列的回呼或其取消後發布行為。

## 原始位置與新位置

以本次修改前的 ImportViewModel.cs 為基準：

| 原始位置 | 新位置 |
| --- | --- |
| ConfirmImportAsync，1074–1184：交易 CTS、世代、進度排程及 finally | Core/Services/ImportExecutionCoordinator.cs：ExecuteAsync、Cancel |
| ConfirmImportAsync：receipt 的三項失敗統計、Warnings.Count、SidecarCommitted 路徑 | ImportExecutionResult.FromReceipt |
| ConfirmImportAsync：資格判斷、初始進度、在地化、登錄檔案、清空工作區 | 仍在 ImportViewModel.ConfirmImportAsync |
| BuildExecutionPlansFromWorkspace，1203–1219 | 原位保留 |
| CancelImport 與 Clear 中的交易取消 | 委派至 ImportExecutionCoordinator.Cancel |

結果統計的判斷條件保持原樣；進度的 switch 與成功／失敗文字格式保留。
交易世代改以每次唯一的 CTS 實例身分判斷，在 UI 排程回呼執行時檢查。
協調器拒絕重疊交易，取消僅傳递 token，必須等執行器（含回滾）結束才允許下一筆。
既有檔案操作、sidecar 與回滾執行器不變。

## 驗證範圍

離線測試涵蓋成功、失敗統計、取消等待、重疊拒絕、例外後重試與延遲進度失效。
既有交易測試繼續驗證檔案及 sidecar 操作。
程式對照不代表 WinUI 操作驗證；尚待人工確認成功清空、失敗保留、
取消期間的回滾狀態及按鈕啟用狀態。
# 第三十二階段：服裝名稱的獨立格式確認（2026-09-13）

- 唯讀查核 KKManager 固定版本 `11b31fcb012c269d6d19c3cf0eef7aae0466ceaf` 的 `KoiCoordCard.ParseKoiClothes` 與建構式：名稱直接取自檔頭，沒有替代名稱。
- 使用不呼叫正式 reader 的 BinaryReader 探針，循 PNG chunk 找到 IEND 後讀取三張本機服裝卡。名稱長度分別為 15、23、0；`sunshine_female.png` 的原始名稱確實為空。
- 結果保存於忽略追蹤的 `artifacts/metadata-probe/coordinate-headers.json`；來源參照保存為文字，未編譯或執行。不需修改產品名稱政策。
- 重現命令：`rtk proxy powershell -NoProfile -Command '& ([scriptblock]::Create((Get-Content artifacts/metadata-probe/check-coordinate-header.ps1 -Raw)))'`，三張皆成功完成。
- 本步僅補格式證據與文件；沿用前步 Core 487 通過紀錄，未重跑 Windows 建置或整合測試。KKManager 實際輸出、CSV 與完整延伸資料比對、人工 UI 驗收仍待驗。
# 第三十三階段：修正共用 CSV 的空服裝名稱（2026-09-13）

- 固定版本 KKManager `CardWindow.cs:862–895` 使用 `card.Name ?? ""`；`KoiCoordCard` 直接保留檔頭名稱。正式 reader 無誤，但共用 `CardMetadataExport.ToCsv` 對空服裝名稱誤用了角色的檔名備援。
- 新增三個正式 reader → CSV 回歸案例。修正前空字串案例失敗，其餘兩個通過；最小修正只對服裝卡保留名稱原值，未改 UI、JSON 或角色備援。
- `rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：490 通過、0 略過。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：102 通過、0 略過。
- `rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`：成功，0 警告、0 錯誤。
- `rtk proxy git -c core.safecrlf=false diff --check` 通過。TRX 分別於 2026-09-13 01:22:13、01:23:02 產生；修正前失敗證據保留於 `artifacts/test-results/coordinate-csv-before`。
- 此為欄位來源比對與自動回歸測試，尚未執行 KKManager 完整輸出比對或人工介面驗收。未推送、未觸發遠端 CI。
# 第三十四階段：獨立比對延伸資料契約與 JSON（2026-09-13）

- 查核固定版本 KKManager `PluginData.cs` 的 MessagePack 契約，以獨立 PNG／卡片標頭讀取與型別化反序列化取得參照資料；未編譯下載來源。
- 正式 reader 的結果以正式 JSON 選項往返序列化後，逐一比對 GUID、版本、欄位鍵及巢狀值，包括二進位內容與各自精度的浮點數。
- 7 張通過、0 差異；其中 2 張無 KKEx，另外 5 張合計 73 個 plugin、238 個頂層資料欄位。8 張超過 64 MiB 上限而未比對，不降低或放寬產品限制。
- 探針初版將 float32 字串用 double 解析，造成 3 張假差異；改用原始精度後全部通過。產品無需修改。
- 命令：`rtk proxy dotnet run --project artifacts/metadata-reference-probe/MetadataReferenceProbe.csproj -c Release -- sample artifacts/metadata-reference-probe/report.json`，結束碼 0。報告保留逐檔 SHA-256，未輸出 payload 值。
- 差異檢查通過。本步未更動正式程式或測試，未重跑前步完整測試／建置。此為獨立契約比對，不是 KKManager 執行結果；超限資料、完整 CSV 輸出與人工 UI 仍待驗。
# 第三十五階段：固定 CSV 大小與文化設定回歸案例（2026-09-13）

- 依固定版本 `FileSize.FromBytes/GetCompactSize/ToString` 查核截斷、單位與四捨五入規則；既有共用實作一致，無需修改。
- 新增 `CardMetadataCsvCompatibilityTests`：12 個固定大小邊界與 2 個完整 14 欄輸出案例，包含 `en-US`、`de-DE` 小數分隔符。使用既有 Core 測試專案，不依賴本機 sample 或網路。
- `rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：504 通過、0 略過；TRX 時間 2026-09-13 01:32:04。
- 本步僅增加測試；Windows 整合 102 項及 Release 建置沿用第三十三階段紀錄，未重跑。完整實際 KKManager 輸出及人工 UI 驗證仍待驗。
# 第三十六階段：共用圖庫解析度篩選更新（2026-09-13）

- 把三個圖庫重複的解析度狀態、啟用判斷與 UI 更新回呼搬到既有 `GalleryViewModelBase`。仍是每個圖庫實例自己的狀態；設定讀取、事件來源及卡片篩選政策未搬移。
- 搬移前：`GalleryViewModel.cs:315–324`、`CharacterGalleryViewModel.cs:332–341`、`CoordinateGalleryViewModel.cs:153–162`；搬移後：`GalleryViewModelBase.cs:83–92`。移除存取修飾及方法名稱差異後，三份原回呼內文與共用方法逐字相同（只正規化換行）。
- 保留 UI 排程、設定集合實例、隨機佇列重建 → 排序 → 篩選的順序，排程拒絕處理也未改。三個事件的訂閱／解除訂閱都轉接同一基底入口。
- Windows 整合：`rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`，102 通過、0 略過；TRX 時間 2026-09-13 01:35:57。
- 建置：`rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`，0 警告、0 錯誤。Core 無變更，沿用第三十五階段 504 通過。
- 實際篩選／隨機模式 UI 操作仍待人工驗證；來源一致性与整合測試不代表完整 XAML 行為已驗收。
# 第三十七階段：共用掃描核心（2026-09-13）

- `rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`：成功，0 警告／0 錯誤；`rtk proxy git -c core.safecrlf=false diff --check` 通過。
- `CardScanService` 的兩個 `ScanFoldersAsync` 原本各自列舉目錄、跳過缺失目錄、平行解析並檢查取消；改呼叫同一個 private `ScanFolders`。沒有新增公開 API 或另一層服務。
- 搬移前 `Services/CardScanService.cs:35–47`、`:80–91`；搬移後同檔 `:79–94`。`ConcurrentBag`、分批鎖定與尾批發布仍由原入口持有；目錄順序、解析器、平行度上限及例外／取消傳遞保持不變。
- 抽取前先新增 9 個正式基底整合案例，涵蓋兩種入口的缺失目錄、拒絕卡片、預先／解析中取消、解析例外、失敗後重試與批次發布例外。測試用暫存檔與繼承的解析委派，沒有假的 WinUI 型別、正式圖庫或固定 sleep。
- 抽取前 9 項通過：`artifacts/test-results/scan-before`，TRX 時間 2026-09-13 01:40:02。抽取後完整 Windows 整合 111 項通過、0 略過：`artifacts/test-results/integration`，TRX 時間 01:40:56。
- 命令：`rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`；抽取前另加 `--filter FullyQualifiedName~CardScanServiceTests` 並使用上述 scan-before 目錄。
- Core 本步未改，沿用 504 項通過。此測試直接涵蓋掃描基底，不代表圖庫 UI 的批次顯示與取消重載已完成人工驗收。
# 第三十八階段：共用可見清單隨機取卡（2026-09-13）

- 四個 `GetRandomCard` 保留公開名稱及具體回傳型別，轉接既有基底的 `GetRandomVisibleCard<TCard>`。空清單回傳 null、Random.Shared、CardsView 索引及 as 轉型政策未變。
- 搬移前：CharacterGalleryViewModel:330–334、CoordinateGalleryViewModel:151–155、GalleryViewModel:314–318、MediaGalleryViewModel:88–92；搬移後：GalleryViewModelBase:83–87。正規化換行並替換具體型別為 TCard 後，四份內文逐字相同。
- 執行 `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：111 通過、0 略過，並完成主程式 Release 編譯；TRX 時間 2026-09-13 01:44:03。未另重跑獨立 build，Core 無變更並沿用 504 項紀錄。
- 實際 UI 隨機開卡仍待驗，不新增假的 XAML host 或只重述實作的測試。卡片服務的建立流程經盤點後保留：註記、建立時間與媒體尺寸的差異不應因共用而消失。
# 第三十九階段：分離解析診斷收集器（2026-09-13）

- 原 `ImportService.cs:946–997` 巢狀類別搬至 `Services/ResolveDiagnosticCollector.cs:6–62`，維持 internal。`ResolveDiagnosticResult` 保留原位置與組件，沒有公開型別搬移或 API 變更。
- 正式預設仍使用 Stopwatch；內部時戳委派供離線測試精確控制時間。不改測量範圍、finally 累計、結果欄位、目的地政策或既有慢解析記錄。此物件仍由單次 UI 發布循序操作，不新增平行／跨輪共享。
- Core 測試以來源連結納入收集器及原結果模型，新增 3 案例涵蓋累計與完整欄位映射、失敗仍計時且傳回原例外、讀取不重設及各轮隔離。沒有 sleep 或 WinUI 替身。
- `rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：507 通過、0 略過，TRX 時間 2026-09-13 01:47:13。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：111 通過、0 略過，包含主程式 Release 編譯；TRX 時間 01:47:48。未另執行獨立 build。
- 差異檢查通過；ImportService 本次僅移除巢狀類別，既有未提交的慢解析記錄修改保留。人工 UI 驗證仍待驗。
# 第四十階段：分離作者索引查詢（2026-09-13）

- `ImportService.ResolveDestinations` 中的精確 scope 查詢及跨 provider／遊戲 fallback 搬至 Core 的 internal `ImportAuthorDirectoryLookup.FindExact/FindFallback`；只接收既有索引與值，不讀磁碟、不引用 Plugin SDK／WinUI、不修改模型。
- 原本 `ImportService.cs:616–660` 的根目錄／provider／遊戲查詢責任，移至 `Core/Services/ImportAuthorDirectoryLookup.cs:6–51`。是否允許 fallback、建立新目標資料夾、未辨識路徑、狀態與診斷仍留在服務。
- 保留第一筆命中政策：先前 provider 的其他遊戲版本命中可先於後續 provider／root 的精確命中；回傳 provider folder 而不誤用其他遊戲的實體資料夾。空 provider folder 和 null 無命中仍有別，沿用索引字典 comparer 與共用 scope key。
- 新增 6 個 Core 案例，涵蓋以上優先性、分級範圍、首次命中後不繼續列舉等。`rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：513 通過，TRX 時間 2026-09-13 01:50:46。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：111 通過，包含主程式 Release 編譯；TRX 時間 01:51:27。皆無略過，差異檢查通過，未另執行獨立 build。
- 未更動現有慢解析記錄與其他未提交修改。人工 UI 驗收仍待完成。
# 第四十一階段：分離指紋比較與影像解碼（2026-09-13）

- 將 `ImageFingerprintService.cs:142–162` 的 HammingDistance／HistogramCorrelation 原樣搬入 Core internal `ImageFingerprintComparison.cs:7–27`。原公開方法保留轉接；CardGroupingService 直接使用純數值模組，不再因比較指紋而依賴 Windows 影像解碼服務。
- 兩個方法內文與搬移前逐字一致。解碼、PHash 計算、直方圖建立與分組門檻未改；只把誤稱 majority 的註解更正為原演算法的 30% 有效配對。
- Core 測試來源連結正式 CardGroupingService，新增 12 個案例涵蓋 64 位距離、退化／正負相關直方圖、同資料夾門檻、缺少指紋三態、30% 判斷及連鎖分組；不引入 WinUI 替身、sample 或網路。
- `rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：525 通過，TRX 時間 2026-09-13 01:57:55。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：111 通過，TRX 時間 01:58:36；包含主程式 Release 編譯。皆無略過，差異檢查通過；未另重跑獨立 build。
- 測試驗證既有分組算法，不代表其視覺品質或實際 UI 已經人工驗收。
# 第四十二階段：分離像素指紋生成（2026-09-13）

- `ImageFingerprintService` 保留 PNG 讀取、32×32 Linear 縮放、BGRA8 Premultiplied 解碼、EXIF／色彩管理選項、取消及錯誤政策；灰階、DCT PHash 與直方圖計算搬入 Core internal `ImageFingerprintPixels`。
- 原 `ImageFingerprintService.cs:63–139` 三個方法搬至 `Core/Services/ImageFingerprintPixels.cs:20–96`；內文逐字一致，未調整浮點運算順序或快取計算。原公開 ImageFingerprint 結果型別仍在原組件，沒有搬移公開 API。
- 搬移前以原數值方法副本產生 3 組固定像素雜湊，保留於忽略追蹤的 `artifacts/fingerprint-baseline`，命令 `rtk proxy dotnet run --project artifacts/fingerprint-baseline/Baseline.csproj -c Release`。種子 1／17／123456 的雜湊分別為 4A01F87A28BCF3E8、5E4C0EFCE242D8CC、043F5499376C5A1D。
- 新增 6 個 Core 案例覆蓋固定基準、黑色影像、RGB 通道及歸一化、輸入不被修改、數值層不重複套用 alpha。
- `rtk proxy dotnet test KoikatsuSceneGallery.Tests/KoikatsuSceneGallery.Tests.csproj -c Release --logger trx --results-directory artifacts/test-results/core`：531 通過，TRX 時間 2026-09-13 02:01:56。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：111 通過，TRX 時間 02:02:50，包含主程式 Release 編譯。皆無略過，差異檢查通過；未另執行獨立 build 或遠端 Linux CI。
- 固定像素驗證不能代替不同圖片的 Windows 解碼品質與人工分組驗收。
# 第四十三階段：驗證 Windows 解碼與 Core 指紋串接（2026-09-13）

- 新增 5 個正式 ImageFingerprintService 整合案例，直接呼叫 Windows BitmapDecoder，無視窗、無 WinUI 替身。使用本機生成的 32×32 純黑／純紅 PNG 常數，沒有外部卡片資料。
- 驗證黑色指紋與直方圖、PNG 後附卡片資料不影響指紋、RGBA → BGRA 通道意義、無效檔案回傳 null、預先取消傳回 OperationCanceledException；讀取不改寫來源。純紅結果也與已知 BGRA 像素的 Core 計算一致。
- 所有等待設有上限；有工作會使用 10 秒取消期限與 15 秒外層等待。測試結束清理單一暫存 PNG，不使用正式圖庫。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：116 通過、0 略過，TRX 時間 2026-09-13 02:07:22；包含主程式 Release 編譯。差異檢查通過。
- 本步僅新增測試，Core 沿用 531 通過，未另執行獨立 build。這些案例證明真實解碼串接可執行，但不涵蓋所有尺寸、色彩描述檔、視覺品質或人工 UI。
# 第四十四階段：建立無視窗 XAML 驗證宿主（2026-09-13）

- 新增 `IntegrationTests/XamlHost` 獨立程序，以與主程式相同版本的 Windows App SDK／Toolkit 集合套件初始化 Application.Start，不建立 Window、不初始化正式 App、不讀設定或圖庫。
- 真正的 AdvancedCollectionView 已驗證延後套用來源增刪、metadata 值變更後刷新篩選與排序，以及清空後重用。資料項目是一般通知物件，不是假 WinUI 類型。
- 既有 Windows 整合測試專案以建置相依納入宿主，複製至輸出目錄的 XamlHost 子目錄；外層 xUnit 啟動不可見子程序，擷取輸出並檢查每個驗證標記。宿主 30 秒、外層 45 秒上限；逾時只終止自己啟動的程序並等待收尾。
- `rtk proxy dotnet test KoikatsuSceneGallery.IntegrationTests/KoikatsuSceneGallery.IntegrationTests.csproj -c Release -p:Platform=x64 -p:WindowsAppSDKSelfContained=true --logger trx --results-directory artifacts/test-results/integration`：117 通過、0 略過，TRX 時間 2026-09-13 02:14:42；包含主程式及宿主 Release 編譯。Core 無變更，沿用 531 項。
- 修正測試宿主資料欄位及 MSBuild 複製路徑後完整測試通過，正式產品程式未修改。CI 原 Windows 整合命令會建置及執行此案例，Linux Core 不引入 WinUI；未觸發遠端 CI。
- 本階段解除第十八階段「沒有 XAML runtime 的集合啟用限制」，但只驗證真實集合行為，尚未直接驗證正式 GalleryViewModelBase 或頁面繫結／按鈕操作。

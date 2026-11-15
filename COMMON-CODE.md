# 共用程式現況與後續邊界

2026-09-13 更新至第五十九階段。這是目前已檢查區域的架構索引，不代表整個專案已完成
模組化；逐階段修改與驗證證據見 IMPORT-REFACTOR.md 及 CLAUDE.md 指向的獨立交接。

## 現有責任分配

| 區域 | 共用入口 | 呼叫端保留的政策 |
| --- | --- | --- |
| 圖庫識別 | Services/LibraryRegistry.cs、Helpers/LibraryAdapter.cs | 各圖庫卡片類型與瀏覽轉接 |
| 圖庫 UI 基礎 | ViewModels/GalleryViewModelBase.cs | 角色最新版本／來源、場景分級、各圖庫篩選捷徑 |
| 解析度篩選更新 | ViewModels/GalleryViewModelBase.OnResolutionFilterChanged | 場景／角色／服裝各讀自己的設定及訂閱各自事件；卡片判定仍留在原圖庫 |
| 隨機取卡 | ViewModels/GalleryViewModelBase.GetRandomVisibleCard | 四個圖庫保留各自公開型別入口；候選來源維持 CardsView，不改用全部 Cards |
| 隨機候選池與輪替 | Core/Services/GalleryShuffleQueue.cs | 基底供應來源、篩選及顯示數；20 張政策、WinUI 排序與通知留在基底。模組管理洗牌、尾段、已使用集合及顯示快照 |
| 掃描與檔案監看 | Services/CardScanService.cs | 各卡片服務的檔案列舉及解析 |
| 一次回傳／分批掃描 | Services/CardScanService.ScanFolders | 共用目錄巡訪、平行解析與取消檢查；兩個公開入口各自保留收集及發布策略 |
| 掃描批次去重加入 | ViewModels/GalleryViewModelBase.PublishScannedCards | 角色／服裝／媒體共用取消檢查、UI 發布及依原字典保留首筆；設定、集合清空、監看、metadata 與場景專屬處理留在呼叫端 |
| 圖庫取消來源釋放 | ViewModels/GalleryViewModelBase.DisposeWorkCancellationSources | 四個 Dispose 共用載入與縮圖 CTS 的取消／釋放順序；metadata、事件解除與其他資源仍由各圖庫負責 |
| metadata 掃描 | Core/Services/MetadataScanCoordinator.cs | 解析、套用、記錄與 UI 排程委派 |
| metadata 快取 | Services/MetadataCacheService.cs | 卡片 parser、快取檔名與 JSON 選項 |
| 檔案版本快取識別碼 | Core/Helpers/FileVersionCacheKey.cs | metadata 與縮圖共用原始路徑＋修改 ticks 的 SHA256 前 16 碼；讀寫、取消、有效性判定與清理仍各自負責 |
| sidecar JSON 原子寫入 | Core/Services/AtomicJsonFile.cs | PostMetadataStore、LocalSourceStore、CharacterAnnotationStore 保留 schema、JSON 選項、目錄、寫入鎖與邏輯變更；共用暫存、flush、替換與清理 |
| 圖庫 metadata 目錄 | Core/Services/LibraryMetadataDirectory.cs | 共用 .scenegallery 名稱與 Windows 隱藏屬性設定；建立／刪除時機、路徑驗證與例外處理留在三個儲存器 |
| 詳細頁重載定位 | Helpers/DetailNavigationHelper.RefreshAfterReload | 頁面保留排程、前景檢查、來源圖庫、返回與場景群組刷新；共用路徑查詢及同／新實例分支 |
| 詳細頁清單導覽 | Helpers/DetailNavigationHelper | 四頁先詢問 ViewerChrome／BrowseContext；三個卡片頁再選群組或 CardsView，截圖頁用 CardsView。索引缺失與隨機策略不得與上下文導覽混同 |
| 詳細頁隨機索引 | Helpers/DetailNavigationHelper.ChooseRandomIndex | IList 與 CardsView 共用多張避開目前索引的重抽；空集合、取項目與型別檢查留在原多載，ViewerChrome 算法不變 |
| 快取持久化 | PluginCommon/DebouncedDiskPersistence.cs、AtomicFileWriter.cs | metadata 與 SceneCardCacheService 各自的 dictionary、序列化及失效規則 |
| 同步操作收尾 | Core/Helpers/OperationDrain.cs | 場景與 metadata 快取共用已接受操作追蹤；停止後的回傳／拒絕政策仍由服務決定 |
| 首次快取載入 | Core/Helpers/CacheLoadGate.cs | 場景遇缺檔／null 可重試，metadata 嘗試後即終結；JSON 與例外記錄仍由服務處理 |
| metadata 定期刷新 | Services/GalleryMetadataRefresh.cs | 場景條件式刷新；角色／服裝無條件刷新 |
| UI 發布等待 | Core/Services/AwaitedUiPublication.cs | 匯入採 async；同步背景掃描採有期限的 blocking 入口 |
| 匯入資料解析 | Services/ImportService.cs、Core/Services/ImportResolutionCoordinator.cs | 服務擷取與套用，協調器合併／取消／發布生命週期 |
| 解析診斷 | Services/ResolveDiagnosticCollector.cs | 收集器只累計單輪計時與計數；測量範圍、模型修改及慢解析記錄留在 ImportService |
| 圖庫索引及作品搜尋 | Core/Services/ImportLibraryIndexer.cs、ArtworkDirectoryLookup.cs | ImportService 與 AuthorPostService 各自保留候選目錄政策 |
| 作者索引查詢 | Core/Services/ImportAuthorDirectoryLookup.cs | 純索引查詢保留搜尋順序；是否允許 fallback、資料夾命名與模型套用留在 ImportService |
| 影像指紋數值比較 | Core/Services/ImageFingerprintComparison.cs | Windows 解碼仍由 ImageFingerprintService 處理；同資料夾門檻、連鎖分組及 30% 配對政策留在 CardGroupingService |
| 像素指紋生成 | Core/Services/ImageFingerprintPixels.cs | 純 32×32 BGRA 數值計算；縮放、解碼、色彩管理、讀檔與取消仍在 Windows ImageFingerprintService |
| 匯入審核 | ViewModels/ImportReviewWorkspace.cs、各 Import*Assignment／EditService | ViewModel 命令、摘要與實際 Items 移除 |
| 本地來源身分 | Core/Models/LocalSourceIdentity.cs | 資料夾格式仍由 ImportDestinationPolicy.FormatAuthorFolder 與既有 AuthorFolderFormat 決定 |
| 本地來源描述與登錄 | Core/Services/LocalSourceStore.cs、Services/LocalSourceRegistry.cs | provider 只查登錄；掃描範圍與 adopted 路徑政策留在 registry |
| 卡片來源判定 | Helpers/CardOrigin.cs、Core/Helpers/CardOriginQuery.cs | 各圖庫的 BaseFilterPasses 自行組合；分組退化由 GalleryGrouping 依 providerId 決定 |
| 本地來源落地目標 | Helpers/LocalSourceTargets.cs | 由 DestinationPath 推導，不可改讀 AuthorDirectoryPath（見下） |
| 本地來源格子清單 | Helpers/LocalSourceTileBuilder.cs | registry 為存在性權威；有卡片時保留 AuthorInfoService 的 AuthorDisplay 實例 |
| 匯入交易序列化 | Core/Services/ImportExecutionCoordinator.cs（App 建一個實例共用） | 兩個匯入頁各自的忙碌訊息與批次語意 |
| 卡片同一性 | Core/Services/CardPayloadHash.cs | 雜湊 PNG 之後的資料，邊界與 parser 共用 PngEmbeddedData.GetPngSize。預覽圖不算卡片的一部分，所以不進雜湊；無卡片資料一律回傳 null，不可讓兩個「都不是卡」讀成相等 |
| 副本檔名 | Core/Helpers/CopySuffix.cs | 只用來擴大「值得比對」的集合，判定永遠由內容決定，所以樣式可以寬鬆；正規化後為空字串時不得用來查表 |
| 刪除卡片 | Helpers/RecycleBin.cs | 唯一會銷毀使用者卡片的路徑，一律走資源回收筒（FOF_ALLOWUNDO），且必須在刪完後確認檔案真的不存在才回傳成功 |
| 詳情頁頁碼 | Helpers/ViewerChrome.FallbackPosition | 卡片不在瀏覽清單裡時 Update／Navigate 都回傳 false，把導覽交還給頁面；不可再印出 0 / N |
| 重複來源的處置 | Core：ImportItemPlan.KeepDuplicateSource | 逐筆計畫而非逐個 executor，因為兩個匯入頁共用同一個 ImportExecutionCoordinator，而它們要的相反：線上匯入清空下載資料夾，本地匯入讀的是使用者留著的朋友資料夾 |
| 重複卡片的來源 | ViewModels/LocalImportViewModel.Duplicates | 兩處判定合併：分析階段的 AlreadyInLibrary（檔案從未被碰過）與交易階段的 DuplicateSourceKept。對使用者是同一件事，必須一起回報 |
| 本地來源改名 | Core/Models/LocalSourceIdentity.RenameFolderName | 只由 LocalSourceRegistry.RenameAsync 呼叫。新資料夾名一律沿用舊資料夾的形狀（不讀設定裡的格式），因為 TryParseFolderId 認得的樣式是資料夾與卡片之間唯一的連結 |
| 資料夾改名的通知 | Services/CardScanService.WatchDirectoryRenames | 卡片 watcher 有副檔名 filter，資料夾名不會命中，所以目錄改名需要第二個 watcher；展開成每張卡的 -舊路徑／+新路徑，列舉在背景執行緒（阻塞 watcher 執行緒會讓它溢出並丟事件） |
| 本地來源編輯流程 | Helpers/LocalSourceEditing.RunAsync | 三個入口共用。順序固定：先寫頭像、再改名（頭像才會隨資料夾一起搬）、最後 RefreshAuthorAsync 讓全 app 的 AuthorDisplay 更新 |
| 本地落地檔名 | Core/Helpers/LocalDestinationNames.cs | 只有本地匯入用它；線上匯入的同名衝突仍刻意整批失敗（ImportExecutionTransactionTests 鎖住）。同名同內容必須保持原檔名，否則重複偵測會被破壞成第二份複本 |
| 匯入失敗訊息 | Core/Helpers/LocalImportFailureText.cs | 取字串仍由呼叫端注入；訊息裡的數量是整批，不是 executor 的 failedCount（那個永遠是 1，因為交易停在第一個失敗） |
| 角色卡使用者註記 | Core/Services/CharacterAnnotationStore.cs | 套用時機留在 CharacterCardService；索引與過濾語意留在 CharacterGalleryViewModel |
| 角色版本排名 | Core/Helpers/CharacterVersionRanker.cs | 分組鍵（EffectiveVersionKey）與事件發送留在呼叫端 |

表中的 Core 路徑前綴為 KoikatsuSceneGallery.Core。責任分配的原則是共用機制，
而有意不同的選取、篩選、路徑及交易政策保留在原有呼叫端。

## 已收斂的流程與後續邊界

2026-09-13 補充盤點：卡片建立的數個欄位指定暫不抽成通用工廠。
CharacterCardService 在建立後套用註記，CoordinateCardService 讀取建立時間，
SceneCardService 不讀建立時間，MediaCardService 不讀 PNG 尺寸。這些差異需要
留在各服務；現有 CardScanService 已共用列舉流程與監看機制，無需為少量 init
欄位額外引入物件快照或可變 setter。

1. **共用 metadata 發布生命週期**：已修正取消／重新掃描後的舊回呼套用；第二十八
   階段補齊 dispatch 拒絕／例外及排隊回呼例外的回報。Scan.Start 任務仍代表解析
   與排程完成，不代表 UI 回呼全部執行；完成回呼被拒絕時，pending 計數保留到
   下一次 Start／Cancel(resetCount: true)，不在背景直接修改 UI 狀態。
2. **圖庫載入工作階段**：第二十九階段新增基底 RunLoadAsync，四個圖庫共用
   BeginLoad、可選 metadata 取消、縮圖重設、IsLoading、刷新 deferral、取消記錄
   與 CardsReloaded。各自的清空、掃描、監看及場景快取預載／版本比對保留。
   媒體圖庫也已沿用 PublishScannedBatch；第四十九階段在真實 WinUI 宿主直接驗證
   基底載入、批次發布等待、交錯取消、失敗收尾與重試。各圖庫完整載入仍待驗。
3. **metadata 持久化可靠性**：第二十階段已讓 MetadataCacheService 沿用現有
   PluginCommon/DebouncedDiskPersistence 與 AtomicFileWriter；主程式直接編譯這兩份
   internal 來源。離線整合測試涵蓋失敗保留 dirty、重試成功、失效移除，以及序列化
   期間變更保留下一輪儲存。快取 schema 未改；程序關閉時的 producer 收尾與最後
   flush 已於第二十二階段串接 AppWindow.Closing，透過 ShutdownCoordinator 共用
   五秒總等待期限。直接破壞視窗的 Closed 路徑僅嘗試非同步收尾；逾時或程序退出
   不保證完成最後寫入。正常關閉與重複關閉的實際 UI 操作仍待人工驗證。
   第二十四階段已將場景快取納入同一五秒期限，直接在快取入口停止寫入，避免
   把掃描、監看與縮圖工作各自的取消政策混在一起。
4. **ImportService 目的地規劃**：索引與作品查詢已共用，ResolveDestinations／
   ResolveArtworkDestinations 仍同時操作模型與查詢磁碟。若繼續拆分，應先把唯讀
   計算與 UI 套用的輸入／輸出定義清楚，守住現有診斷與服務整合斷言。
   第三十九／四十階段已獨立診斷收集器與作者索引查詢。剩下的資料夾命名、作品
   分組及模型狀態套用具有順序關係；進一步拆分前，應先確認可觀察行為與資料快照
   邊界，不能只為縮短 ImportService 把相同耦合搬到另一個大類別。

## 本地卡片收藏（Local Sources）

`Services/LocalSourceProvider.cs` 是內建（非 plugin DLL）的 `ProviderId = "local"`
provider，同時實作 `IFolderAuthorProvider`、`ICardImportProvider` 與
`IImportDestinationProvider`，由 `PluginService.RegisterBuiltInProvider` 在
`LoadPlugins()` 之後 append 到 provider 清單末端。因此本地來源沿用既有作者機制取得
徽章、詳情頁、AuthorsPage 的 per-provider pivot、scoped 瀏覽與匯入目的地規劃，
沒有新的圖庫程式碼路徑。

必須維持的三個約束：

1. **provider 順序**：內建 provider 一律 append 在最後。`ImportService.CreateManualArtworkId`
   的 fallback 取第 0 個 provider，插到前面會讓已安裝 plugin 失去該位置。
2. **嚴格資料夾標記**：`AuthorInfoService.GetCandidateProviders` 在目錄不屬於任何
   provider scope 時會退回試所有 provider，所以 `LocalSourceIdentity` 的
   `local-` 前綴與非純數字 id 是唯一防止誤判的界線，反向斷言必須保留。
3. **模式先於檔案**：`ImportAnalysisOptions` 必須在拖入前決定。`AnalyzeAsync` 第一階段
   就解析檔名、第二階段立即 fetch，事後標記無法收回已發出的請求。

`ImportProviders` 因此恆非空，`App.xaml.cs` 的 `if (ImportProviders.Count > 0)` 不再
是「有裝 plugin」的同義詞。這是刻意的行為變更：純本地使用者也該有匯入頁。
同理 `AuthorInfoService.IsAvailable` 也恆為真，所以作者頁 nav item 的可見性改看
`AuthorsViewModel.HasProviderTabs`。

## 本地收藏頁（拖曳匯入）

本地匯入在 `Pages/LocalSourcesPage`，與線上匯入頁完全分開：`ViewModels/LocalImportViewModel.cs`
自己持有項目集合、沒有審核工作區，放到某個來源上就是提交。作者頁不再有 Local 分頁
（`AuthorsViewModel` 在建分頁時過濾掉 local provider）。

三個必須維持的約束：

1. **兩個匯入頁不能共用 `ImportService` 實例。** `ImportResolutionCoordinator.RequestAsync`
   會用最新請求取代 pending 的那個，且所有等待者共用同一個 `_round`；它是為「單一擁有者
   合併自己的重複請求」設計的。共用會讓其中一頁的目的地解析被靜默丟棄。所以 `App.xaml.cs`
   建兩個 `ImportService`，但**共用** `LibraryFileCache` 與 `PostMetadataStore`。
2. **`ImportExecutionCoordinator` 反過來要共用**，因為它每個實例只允許一個交易，
   共用就免費得到序列化——兩個交易同時搬檔進同一個作者資料夾是最該避免的失敗。
   忙碌訊息因此要中性措辭，阻塞者可能是另一頁。
3. **`LocalSourceTargets.Collect` 必須由 `DestinationPath` 推導。** 目的地解析只在項目
   有 artwork 身分時才填 `AuthorDirectoryPath`，而本地卡片永遠沒有——改讀那個欄位會讓
   `source.json` 靜默不寫。已由 `LocalImportTests.ImportRecordsTheSourceDescriptionInTheDestinationFolder`
   鎖住。

staging 階段只做 `CardTypeClassifier.ClassifyExtended`（剔除非卡片、讓待匯入數字誠實），
`AnalyzeAsync` 要等到指派時才跑——因為 `ImportAnalysisOptions` 必須在讀檔前就知道歸屬。

## 角色卡版本標記

使用者可以標註一張角色卡的**類型**（正式版／IF 版）與**是否已被更新取代**（兩個獨立面向）、
加一行備註、並手動歸到別的角色底下。

類型與「已取代」刻意分開：單一的三選一無法表達「這是 IF 版，而且已被更新的 IF 取代」——
而朋友會更新自己的 IF 版，就跟更新本尊一樣。舊 sidecar 的 `label: "old"` 仍讀成
「正式版 + 已取代」（`CharacterVersionKinds.IsLegacySuperseded`），不需要 schema 升版。
存在 `{卡片所在目錄}/.scenegallery/characters.json`（`CharacterAnnotationStore`），
沿用 `.scenegallery/` 那套原子寫。

四個必須維持的約束：

1. **標記只能在 `CharacterCardService.TryCreateCard(FileInfo)` 套用。** 那是平行掃描與
   `FileSystemWatcher` 唯一的收斂點，且已在背景執行緒。搬到 `ApplyMetadata` 會變成在
   UI 執行緒做檔案 I/O。
2. **每目錄讀一次，不是每卡讀一次。** store 有 per-directory 快取；
   `CharacterAnnotationScanTests.ScanningReadsOneDocumentPerDirectoryNotPerCard` 鎖住它。
3. **`label` 存字串並寬容解析**，不要換成 `JsonStringEnumConverter`：後者遇到未知值會讓
   整份文件反序列化失敗，等於整個目錄的標記一起消失。
4. **`_versionIndex` 的移除只能讀 `CharacterCard.IndexedVersionKey`**，不可再由角色名反推——
   分組鍵可被 `CharacterGroupKey` 覆寫，反推會留下殭屍 entry。這也是 `_versionIndex` 能改成
   `OrdinalIgnoreCase` 的前提。

「哪張卡代表這個角色」在 `CharacterVersionRanker.SortAndFindPrimary`：最新的「未取代正式版」→
沒有就最新的未取代卡 → 全部已取代就取最新。三段 fallback 保證角色不會因為標記而從圖庫消失。

**「一個角色永遠只佔一格」**：圖庫只顯示代表卡，過濾兩處都是 `card.IsLatestVersion`。
標記不影響可見性——曾經讓「未取代的 IF 版」也各佔一格，結果一個角色被檔名排序拆散在
圖庫各處，比不標記更糟。唯一例外是搜尋：`CardMetadataQuery.NoteMatches` 讓寫在被隱藏
版本上的備註仍能把那張卡找出來，否則那種備註沒有任何用途。

標記剩下兩個行為效果：**誰是代表卡**（`SortAndFindPrimary`）與**徽章要不要顯示 IF**
（`CountsAsLiveAlternate`，已取代的 IF 不計入）。`RankGroup` 在同一個迴圈裡把
`AlternateVersionCount` 設給全組每張卡，所以只顯示代表卡的格子仍能說出「這個角色有 IF 版」。

## 尚未完成的驗收

- 真實 DispatcherQueue 的 metadata timer 與發布等待已有整合測試。
- 第四十四階段新增獨立 XAML 測試宿主，在不建立 Window 的情況下初始化 WinUI，
  真實 AdvancedCollectionView 的延後刷新、篩選／排序與清空重用已自動驗證。
  第四十八階段補上正式 GalleryViewModelBase 組件測試：解析度隔離、可見隨機取卡、
  隨機候選重建／輪替、排序切換及空集合已通過。測試衍生類只提供抽象篩選轉接，
  不代表四個正式衍生圖庫的所有政策或頁面操作均已受測。
- 三個圖庫載入、排序／篩選、取消重載，以及匯入按鈕／群組導航仍待人工驗證。
- 本機 TRX 與遠端 CI 證據分開；本次沒有推送或觸發遠端執行。

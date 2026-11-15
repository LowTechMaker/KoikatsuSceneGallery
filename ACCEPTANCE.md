# 共用化驗收矩陣

## 目前驗收快照（第 59 部分）

2026-09-13 以目前工作樹重新執行：Core 551 項、Windows 整合 140 項全部通過，0 失敗、0 略過。完整命令、TRX、責任範圍與待驗事項見 [第 59 份交接](docs/handoffs/phase-59.md)。測試總數來自實際 TRX；單一 XAML 宿主內的多組檢查沒有重複計成多個 xUnit 案例。

已建立的共用入口及呼叫端政策見 [COMMON-CODE.md](COMMON-CODE.md)。已完成的是這些具體機制的整理與列出的自動驗證，不能推論整個專案已消除所有重複或完成人工操作驗收。

尚待驗證：正式頁面的按鈕／繫結／Frame 導覽、完整圖庫操作與關閉流程、KKManager 實際執行輸出對照。遠端 CI 未觸發，Linux 本機未執行。交接已讀標記由 Claude 實際閱讀後填寫，目前全部維持待讀。

## 歷史驗收紀錄

以下保留各階段當時的結果與限制；較舊通過數及當時未完成項目須以上方快照與最新交接補充判讀，不代表本輪重新驗證。

第 58 部分最新證據：四個圖庫共用取消來源釋放後，Windows 整合 140 項通過。真實 WinUI 宿主驗證取消顺序、CTS 已釋放以及未完成載入稍後安全收尾；不代表背景 I/O 已全部停止或程序關閉已人工驗收。詳見 [第 58 份交接](docs/handoffs/phase-58.md)。

第 57 部分最新證據：角色／服裝／媒體共用掃描批次加入後，Windows 整合 140 項通過。真實 UI 宿主直接驗證同批／跨批去重、順序、實例保留與取消；各正式圖庫完整頁面載入仍待驗。詳見 [第 57 份交接](docs/handoffs/phase-57.md)。

第 56 部分最新證據：IList 與 CardsView 共用詳細頁隨機重抽步驟後，Windows 整合 140 項通過，保留第 54／55 部分的直接入口斷言。詳見 [第 56 份交接](docs/handoffs/phase-56.md)。

第 55 部分最新證據：真實 WinUI 宿主直接測試詳細頁的 CardsView 導覽多載，包含可見順序、隨機、移除相鄰項目與型別檢查；Windows 整合 140 項通過。仍非按鈕或 Frame 人工驗收。詳見 [第 55 份交接](docs/handoffs/phase-55.md)。

第 54 部分最新證據：新增 13 個正式清單導覽入口回歸案例，Windows 整合 140 項通過；產品算法未修改。只驗證 IList 入口，不擴大宣稱 CardsView 或 Frame／按鈕已驗收。詳見 [第 54 份交接](docs/handoffs/phase-54.md)。

第 53 部分最新證據：四個詳細頁共用重載後卡片定位，Core 551 項、Windows 整合 127 項通過。新增八個直接 helper 案例；頁面前景切換與 Frame 返回仍是人工待驗範圍。詳見 [第 53 份交接](docs/handoffs/phase-53.md)。

第 52 部分最新證據：共用 `.scenegallery` 名稱與隱藏屬性處理後，Core 551 項、Windows 整合 119 項通過；三個儲存器的原路徑及隱藏屬性斷言保持不變。詳見 [第 52 份交接](docs/handoffs/phase-52.md)。

第 51 部分最新證據：三個 sidecar 儲存器共用 JSON 原子寫入後，Core 551 項、Windows 整合 119 項通過；新增成功、序列化失敗、取消與替換失敗清理測試。詳見 [第 51 份交接](docs/handoffs/phase-51.md)。

第 50 部分最新證據：metadata／縮圖共用既有快取識別碼計算後，Core 546 項、Windows 整合 119 項通過。新增固定識別碼與正式服務既有快取讀寫案例，確認無須遷移。詳見 [第 50 份交接](docs/handoffs/phase-50.md)。

第 49 部分最新證據：在真實 WinUI 宿主增加正式共用載入流程檢查，Windows 整合 117 項通過。驗證批次發布等待、交錯載入取消隔離、失敗收尾與重試；仍是同一宿主測試內增加斷言，並非新增三個 xUnit 案例。詳見 [第 49 份交接](docs/handoffs/phase-49.md)。

第 48 部分最新證據：正式 GalleryViewModelBase 已能在獨立無視窗 WinUI 宿主中測試；補上 XAML 型別提供者後，從乾淨宿主輸出重建，Windows 整合 117 項通過。涵蓋解析度隔離、可見隨機取卡、候選重建／輪替、排序切換及空集合。第 45／47 部分記載的宿主失敗已由本次驗證補足；頁面操作仍待驗。詳見 [第 48 份交接](docs/handoffs/phase-48.md)。

最新補充（2026-09-13，第 47 部分）：共用隨機佇列抽取後，本機 Core 541 項、Windows 整合 117 項通過；獨立 x64 Release 建置 0 警告、0 錯誤。新增 10 項直接驗證候選池及輪替，不代表四個圖庫的 XAML 操作已驗證。詳見 [第 47 份交接](docs/handoffs/phase-47.md)。以下歷史快照保留。

補充證據（2026-09-13）：正式 reader 已唯讀解析本機 sample 15 張真實卡片，
摘要／完整模式基本欄位一致；8 張因 KKEx 大小限制標記 partial。報告與命令見
IMPORT-REFACTOR.md 第三十一階段。這尚不等同 KKManager 獨立輸出對照。
當日 Core 完整測試為 487 通過；下列 9 月 8 日快照保留為歷史證據。

2026-09-08 第二十七階段的工作樹驗證快照；後續結果見 IMPORT-REFACTOR.md。
此表區分已實作、測試覆蓋與尚未取得的證據；
通過部分測試不等於整體共用化目標已完成。沒有提交、推送或遠端 CI 結果。

## 自動驗證證據

2026-09-10 以卡片資料雜湊辨識副本、版本清單可刪除、頁碼 0/N 修正完成後，本機重新
執行：Core 482／482 通過、Windows 102／102 通過、Windows x64 Release 建置 0 警告
0 錯誤；三份 resw 各 613 鍵（ResourceParityTests 鎖住）。

同日較早的重複卡片保留在原處並彈窗指名（含開啟所在資料夾）為 Core 456／456、
Windows 102／102、resw 各 606 鍵。

同日較早的本地來源名稱與頭像編輯（含資料夾一併改名）為 Core 454／454、
Windows 100／100、resw 各 601 鍵。

同日較早的本地匯入同名衝突處理（同名不同內容自動加後綴、失敗訊息指名卡片）為
Core 437／437、Windows 91／91、resw 各 584 鍵。
在此之前的角色卡版本標記（類型／已取代兩個面向，且改成「一個角色一格」）為
Core 420／420、Windows 88／88。下方 TRX 段落是本地
收藏加入前第二十七階段的快照，兩者不可互相取代。

本輪直接讀取下列 TRX 的 ResultSummary 及 TestDefinitions，沒有重新執行測試：

- Core：artifacts/test-results/core/alexc_DESKTOP-KI82NU6_2026-09-08_12_19_42_net10.0.trx，325／325 通過，0 未執行。
- Windows：artifacts/test-results/integration/alexc_DESKTOP-KI82NU6_2026-09-08_12_20_53_net10.0.trx，40／40 通過，0 未執行。
- 最近獨立 Windows x64 Release 建置：第二十六階段成功，0 警告、0 錯誤。
- .github/workflows/build.yml 已配置 Linux Core 及 Windows Core／整合命令、TRX 與 always 保存結果。配置不代表遠端執行通過。

| 要求／責任 | 現有實作及直接測試 | 證據界線 |
| --- | --- | --- |
| 審核集合、篩選、選取、列重用 | ImportReviewWorkspaceTests 13、ImportReviewLayoutTests 10、ImportReviewPolicyTests 20 | 可控排程及模型；不是 XAML 繫結驗證 |
| 指派、復原、審核編輯 | ImportAuthorAssignmentTests 14、ImportArtworkAssignmentTests 6、ImportSearchResultAssignmentTests 6、ImportReviewEditServiceTests 21 | 不操作真實搬移；命令按鈕仍待人工 |
| 解析合併與取消 | ImportResolutionCoordinatorTests 11、ImportAnalysisTrackerTests 6、Windows ImportServiceTests 25 | 正式服務、離線 provider／暫存資料；非真實網路查詢 |
| 圖庫索引與作品目錄共用 | ImportLibraryIndexerTests 7、ArtworkDirectoryLookupTests 9；上述服務測試含呼叫端差異 | 保留排序／排除差異；GetKnownAuthors 舊式搜尋未合併 |
| KKManager 格式 metadata | CardMetadataTests 15，既有角色／服裝 parser 測試仍保留 | 合成 Parameter／About／KKEx；尚未與真實卡片的 KKManager 顯示及 CSV 比對 |
| metadata 掃描及 UI 發布 | MetadataScanCoordinatorTests 11、BlockingUiPublicationTests 5、真實 DispatcherQueue 整合 2 | 真實佇列及計時器；沒有完整 XAML host |
| 快取持久化與讀取政策 | Windows MetadataCachePersistenceTests 7、SceneCardCachePersistenceTests 6；CacheLoadGateTests 3 | 記憶體 writer／唯一暫存 JSON，不是使用者正式快取 |
| 停止、drain、最後寫入順序 | OperationDrainTests 2、ShutdownCoordinatorTests 2、上述快取整合案例 | 有限等待、忽略取消與延後釋放；無程序退出／視窗事件人工驗證 |
| 本地來源識別與描述 | LocalSourceIdentityTests 10、LocalSourceStoreTests 9、Windows LocalSourceRegistryTests 13 | 嚴格資料夾標記含 fake numeric provider 的反向斷言；真實圖庫掃描仍待人工 |
| 本地批次零對外查詢 | Windows LocalImportTests 8（含線上批次對照組） | 以計次 provider 斷言 fetch／檔名解析次數為 0；不是實際斷網證據 |
| 本地卡片不被當成遠端貼文 | GalleryGroupingTests 2、ImportDestinationPolicyTests 7（UsesUnrecognizedSink） | 純函式；detail 頁按鈕與 Posts tab 的隱藏仍待人工 |
| 三份 resw 鍵一致 | ResourceParityTests 2 | 檢查鍵集合、重複與空值；不檢查譯文品質 |
| 本地來源描述由匯入本身寫入 | Windows LocalImportTests.ImportRecordsTheSourceDescriptionInTheDestinationFolder | 這個案例在修復前確實失敗；上一輪的測試繞過交易所以沒抓到 |
| staging → 指派 → 匯入 | Windows LocalImportViewModelTests 10 | 真實檔案系統與真實交易；同步 publish 委派，非真實 dispatcher |
| 本地來源格子的清單組成 | Windows LocalSourceTileBuilderTests 7 | 純函式：零卡片來源仍上牆、依 id join、既有 display 不被合成取代 |
| 誰代表一個角色 | CharacterVersionRankerTests 15 | 補上先前完全沒有測試的版本邏輯；一個角色只佔一格（含多個 IF 的情況）、IF 不奪冠、全部已取代仍有代表；CountsAsLiveAlternate 真值表窮舉 |
| 舊 sidecar 相容 | CharacterAnnotationStoreTests（legacy "old" token） | 拆成兩個面向前寫的 label 仍讀成「正式版 + 已取代」 |
| 版本標記的持久化 | CharacterAnnotationStoreTests 12 | 壞檔不拋、未知 label 只降級一張卡、清空即刪檔、孤兒 entry 保留 |
| 標記讀取是每目錄一次 | Windows CharacterAnnotationScanTests 3 | 40 張卡分兩目錄 → 讀檔 2 次；直接鎖住掃描路徑的效能約束 |
| 以卡片資料辨識同一張卡 | CardPayloadHashTests 6 | 雜湊 PNG 之後的卡片資料而非整個檔案：預覽圖被重新編碼、檔案大小不同，仍認得是同一張卡；非卡片的 PNG 永遠不相等 |
| 副本檔名正規化 | CopySuffixTests 17 | `(1)`、`_2`、`- Copy`、`- 複製`、`- 副本`、`- コピー` 與疊加的組合；`(1).png` 這種整個名字就是標記的不可被吃掉；Koikatsu 的匯出時間戳不可被誤判 |
| 索引比對涵蓋副本 | ImportLibraryIndexerTests 3 | 回報案例（`(1)` 副本）現在會被比對並判成重複；名字像副本但內容不同的仍照常匯入；既有的精確檔名比對計數未變 |
| 重複卡片保留在原處 | ImportExecutionTransactionTests 2、Windows LocalImportViewModelTests 3 | KeepDuplicateSource 只由本地匯入設；線上匯入仍刪除來源（既有斷言未動）；回滾不會把保留的重複當成需手動處理 |
| 重複卡片的回報 | Windows LocalImportViewModelTests 3 | 分析階段判定的「已在圖庫」與交易階段判定的重複合併成一份清單；混合批次會匯入其餘卡片並指名留下的那些 |
| 本地來源改名的資料夾名稱 | LocalSourceRenameNameTests 12 | 使用者輸入什麼都必須解析回同一個 id；路徑分隔符不得逃出資料夾；名稱清乾淨後為空則不動資料夾 |
| 改名搬資料夾與頭像寫入 | Windows LocalSourceRegistryTests 8 | 連續改名跟得上剛搬走的資料夾；搬不動時只回報、名稱仍生效；頭像存在來源folder內、換格式不留孤兒、隨改名一起搬 |
| 資料夾改名後圖庫跟上 | Windows CardWatcherRenameTests 2 | 目錄改名展開成每張卡的移除＋新增；單檔改名行為不變 |
| 本地匯入的同名衝突 | LocalDestinationNamesTests 11、Windows LocalImportViewModelTests 3 | 同名不同內容加後綴、同名同內容仍折成重複；批次內與圖庫內兩種衝突都涵蓋 |
| 本地匯入失敗訊息 | LocalImportFailureTextTests 7 | 指名卡片與原因、數量對應整批；每個 TransactionFailureType 都有對應理由鍵 |
| 備註可搜尋 | CardMetadataTests 2 | 含「無備註時與舊行為一致」的迴歸斷言 |

此表列主要相關測試，不是所有 325 個案例的重複清單。其他目的地、交易、PNG、
路徑、分組、縮圖等既有斷言仍包含在完整 Core 結果中。

## 尚未取得的必要證據

| 驗收操作 | 預期與記錄方式 | 狀態 |
| --- | --- | --- |
| 真實角色／服裝卡片對照 KKManager | 使用副本，記錄 KKManager 版本、卡片雜湊、欄位及 CSV 差異；不得將 synthetic fixtures 當真實語料 | 尚未執行 |
| metadata panel 切卡／切頁、樹狀展開與鍵盤 | 舊卡資料不回流；展開及分頁可用；記錄版本與操作結果 | 尚未執行 |
| 版本清單刪除與顯示 | 右鍵「刪除這個版本…」→ 確認後檔案進資源回收筒、清單與頁碼立即更新；刪到只剩零個版本時退回上一頁；檔案被占用時顯示失敗訊息 | 尚未執行 |
| 頁碼不再出現 0 / N | 從版本清單開啟非代表卡時，頁碼顯示該角色的版本序號，上一張／下一張改由頁面自己的邏輯處理 | 尚未執行 |
| 重複彈窗與開啟資料夾 | 混合批次匯入後彈窗列出重複的檔名；按「開啟檔案所在資料夾」時檔案總管開啟並選取那些檔案；跨多個來源資料夾時最多開 3 個 | 尚未執行 |
| 本地來源編輯的三個入口 | 本地收藏頁格子右鍵、作者頁 Local 分頁右鍵、作者詳情頁標題按鈕都能開啟同一個對話框；非本地作者看不到入口 | 尚未執行 |
| 改名後的實機表現 | 圖庫、作者頁、詳情頁都顯示新名字；檔案總管裡資料夾也已改名；卡片縮圖重新產生後仍正確 | 尚未執行 |
| 頭像三種來源 | 選圖檔／從卡片挑／從線上作者挑都能設定；清除後回到字母圓標；換格式不留舊檔 | 尚未執行 |
| 實機重跑回報中的 56 張批次 | 不再出現「匯入失敗，1 張卡片保留在原處」；同名卡以 `_1` 落地或指名回報 | 尚未執行 |
| 匯入連續兩批、快速改解析選項、清空再加 | 有效批次完成、最新解析發布、舊項目不回流 | 尚未執行 |
| 審核篩選、選取數、群組導航、手動編輯後解析 | 保留原使用流程與命令啟用；詳見 IMPORT-REFACTOR 第十階段清單 | 尚未執行 |
| 三個圖庫逐批載入、排序／篩選與取消重載 | UI 反映實際卡片；角色版本／來源及場景分級規則保留 | 尚未執行 |
| 本地來源在作者頁與詳情頁的呈現 | Local pivot 出現、作者詳情頁 scoped 瀏覽可用、無「開啟個人頁」、無 Posts tab | 尚未執行 |
| 本地卡片詳情頁無 pixiv／BepisDB 按鈕 | 檔名含 8 位數字或 KKSCENE_ 的本地卡不得出現外連按鈕 | 尚未執行 |
| 角色詳情頁的版本標記區渲染 | 新增的 ComboBox／TextBox／Button 的 x:Uid 後綴在 measure 期不炸；徽章與備註顯示正確 | 尚未執行 |
| 標記後圖庫即時更新 | 標成 IF 版 → 本尊與 IF 版同時出現；勾「已被更新取代」→ 從圖庫消失但仍在版本清單 | 尚未執行 |
| 一個角色只佔一格 | 有多個 IF 版的角色在圖庫只出現一格、帶 IF 徽章；把唯一的 IF 勾成已取代後徽章消失 | 尚未執行 |
| 手動歸組 | 改過名的卡歸入本尊後出現在其版本清單；清除歸組可還原 | 尚未執行 |
| 歸組 picker 的作者範圍 | 預設只列同一位作者的角色；勾「顯示所有作者」才出現其他人的；作者無其他角色時自動顯示全部 | 尚未執行 |
| 備註輸入時左右鍵不切卡 | IsAnnotating 守衛生效；切卡時不誤寫標記 | 尚未執行 |
| 本地收藏頁的拖曳手勢 | 拖入檔案→縮圖列與計數正確；抓起資料夾→飛入動畫；放到作者→hover 高亮、本地化 caption、匯入完成 | 尚未執行 |
| 放到「＋新增來源」格子 | 先問名稱再匯入；新來源立刻成為可放置目標 | 尚未執行 |
| 資料夾右鍵「匯入到…」的鍵盤路徑 | 不需滑鼠拖曳即可完成匯入；含 Narrator | 尚未執行 |
| 關閉系統動畫後的退化行為 | 縮圖列直接收起，拖放與匯入行為完全一致 | 尚未執行 |
| Esc 取消拖曳 | 反向動畫還原，staging 未變 | 尚未執行 |
| 匯入頁已無本地 UI、作者頁已無 Local 分頁 | 兩處都不再出現本地相關控件；沒裝線上作者 plugin 時作者頁 nav item 隱藏 | 尚未執行 |
| 斷網下走完本地匯入與瀏覽 | 全程無例外、無請求；這是「零對外查詢」的唯一實機證據 | 尚未執行 |
| 圖庫來源篩選與徽章計數 | 全部／排除本地／僅本地三值切換正確，filterCount 徽章加一 | 尚未執行 |
| 未安裝任何 plugin 時啟動 | 內建 local provider 讓 ImportProviders 恆非空；匯入頁存在且不崩 | 尚未執行 |
| 背景解析時關閉與重複關閉 | 只啟動一輪收尾、正常路徑最後寫入；逾時有診斷並允許關閉 | 尚未執行 |
| AdvancedCollectionView 的完整刷新測試 | 需要正確初始化 XAML 的測試 host；目前在 CurrentChangingEventArgs 啟用失敗 | 尚未完成 |
| 遠端 CI | 使用實際遠端 job／TRX 證據；本次不觸發 | 尚未執行 |

## 後續實作選擇

目前還有可具體檢查的缺口：MetadataScanCoordinator 對 dispatcher 拒絕及 apply
例外的處理；圖庫載入工作的共同生命週期；ImportService 中目的地規劃與套用的
邊界。這些是後續候選，不是本文件宣稱已完成的項目。優先修正有失敗案例或多個
真正使用者的共用責任，避免為減少行數而消除政策差異。
## 2026-09-13 CSV 修正後驗證

- 自動測試通過：Core／元件 490 項、Windows 正式服務整合 102 項，均無略過。命令與 TRX 路徑見 `IMPORT-REFACTOR.md` 第三十三階段。
- Windows x64 Release 建置成功：0 警告、0 錯誤；差異檢查通過。
- 新增空白、僅空格與非空服裝名稱的 CSV 回歸測試；修正前空白案例確實失敗，修正後通過。
- 尚未執行：KKManager 實際 CSV／延伸資料完整輸出比對，以及既有人工介面清單。來源格式比對不能替代這些驗收。
## 2026-09-13 延伸 metadata 契約比對

- 本機自動探針通過：7 張卡片（含 2 張無延伸資料），其餘 5 張的 73 個 plugin／238 個頂層欄位及巢狀值，經正式 JSON 選項往返後與獨立型別化解碼一致。
- 尚未比對：8 張超過既有 64 MiB 區塊限制的樣本。
- 這是固定來源契約的獨立解碼比較，並非 KKManager 程式執行或人工 UI 通過。詳細命令、報告與限制見 `METADATA.md` 及第三十四階段紀錄。
## 2026-09-13 CSV 固定格式回歸

- 自動測試通過：Core／元件 504 項，0 略過；新增 12 個大小邊界及 2 個跨文化完整 CSV 案例。
- 本步僅測試變更，Windows 整合／Release 建置沿用第三十三階段結果。未執行 KKManager 實際程式或人工 UI。
## 2026-09-13 解析度篩選共用化

- 自動驗證通過：三份原回呼與基底共用回呼內文一致；Windows 整合 102 項通過、x64 Release 建置 0 警告／0 錯誤。Core 沿用前階段 504 項。
- 人工待驗：分別在場景、角色與服裝圖庫切換解析度設定，確認只有對應圖庫套用；切至隨機模式後再變更設定，確認可見卡片與隨機佇列符合新條件；停用或清空允許清單後不限制解析度。
## 2026-09-13 掃描核心共用化

- 自動測試通過：9 個新增掃描基底案例在搬移前通過；搬移後完整 Windows 整合 111 項通過、0 略過。
- 測試涵蓋兩個掃描入口的取消、拒絕、錯誤與重試。Core 沿用前階段 504 項；實際圖庫 UI 掃描、取消重載仍待人工驗證。
## 2026-09-13 可見清單隨機取卡

- 自動驗證通過：四份取卡方法經型別替換後與共用方法內文一致；Windows 整合 111 項通過，包含主程式 Release 編譯。
- 人工待驗：四個圖庫套用篩選後隨機開卡，只能開到可見清單內的卡片；可見清單為空時不得開啟卡片。
## 2026-09-13 解析診斷收集器

- 自動測試通過：Core／元件 507 項、Windows 整合 111 項，均無略過；包含主程式 Release 編譯及差異檢查。
- 新增診斷測試驗證確切計時累加、錯誤傳遞、各輪隔離與結果欄位映射；不將此結果視為人工 UI 已驗收。
## 2026-09-13 作者索引查詢

- 自動測試通過：Core／元件 513 項、Windows 整合 111 項，均無略過；包含主程式 Release 編譯與差異檢查。
- 新增 6 案例固定作者查詢的 scope、順序與 fallback 政策；完整 UI 操作仍待人工驗收。
# 目前驗收摘要（2026-09-13，第 45 部分交接制度建立後）

個別交接已補建於 `docs/handoffs/phase-01.md` 至 `phase-45.md`，直接索引及 Claude 已讀規則在 CLAUDE.md。所有已讀欄目前仍未勾選。

此表是目前工作樹的最新驗證；下方各階段的較小測試數為歷史紀錄，不代表退步或缺測。

| 驗收範圍 | 證據 | 結果 |
| --- | --- | --- |
| Core／元件 | `artifacts/test-results/core/alexc_DESKTOP-KI82NU6_2026-09-13_02_01_56_net10.0.trx` | 531 通過，0 失敗、0 略過 |
| Windows 正式服務整合 | `artifacts/test-results/integration/alexc_DESKTOP-KI82NU6_2026-09-13_02_24_12_net10.0.trx` | 117 通過，0 失敗、0 略過；撤回未完成的正式基底測試擴充後，從乾淨宿主輸出重新建置 |
| 真實 XAML 集合 | XamlCollectionHostTests：獨立無視窗程序 | 延後刷新、metadata 篩選／排序及清空重用通過；尚非正式圖庫 ViewModel／頁面驗證 |
| 真實 Windows PNG 解碼 | ImageFingerprintServiceTests 的 5 個案例 | 純色像素、通道、卡片尾端、錯誤與取消通過；非人工 UI 驗收 |
| 最新獨立 Release 建置 | `rtk proxy dotnet build KoikatsuSceneGallery.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64`，第四十階段後執行 | 成功，0 警告、0 錯誤 |
| 測試納入 | TRX 中核對作者索引 6、診斷 3、CSV 相容性 14、掃描基底 9 個案例；主專案排除測試與 artifacts 目錄 | 已確認 |
| CI 設定 | `.github/workflows/build.yml`：Linux Core、Windows Core＋整合、`if: always()` 保存 TRX | 靜態查核通過；未執行遠端 CI |
| metadata 真實樣本 | 15 張基本讀取；7 張獨立延伸契約比對，8 張超限未比對 | 有限範圍通過，非 KKManager 實際執行 |
| 人工 UI | 下方操作清單 | 尚未執行 |

主要未完成項目：實際圖庫／匯入 UI 操作、正常關閉與重複關閉，以及 KKManager
實際輸出比對。8 張超限樣本未做完整延伸解析；這是既有資源限制，不因未比對而擅自放寬。
本輪未推送、未使用正式圖庫、未執行真實匯入搬移。不能把這份摘要當成全專案已完成模組化的聲明。

# 新插件協作規則

先閱讀 [ARCHITECTURE](ARCHITECTURE.md) 與 [HANDOFF](HANDOFF.md)，再修改程式。保留未提交變更。

- 入口只適配 SDK／建立依賴；解析器不得做 I/O；供應商政策由本插件負責。
- SDK 只透過固定版 compile asset 引用，宿主提供 runtime；不要加入宿主 ProjectReference。
- 新增真實 HTTP 時採 injectable HttpMessageHandler；測試完全離線。需要共享請求／資源時採 Common source-only 套件與完整 producer shutdown scope，勿把 caller token 傳給 shared producer。
- 更改公開契約、格式、依賴方向時記錄架構決策與相容性證據，不可只刪除測試。
- 執行 `pwsh -NoProfile -File eng/Verify-Plugin.ps1`，將實際結果與未驗證項目寫入 HANDOFF。不得自動發布。

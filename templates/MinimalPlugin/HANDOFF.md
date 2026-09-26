# 最小插件範例交接

版本 v1，2026-09-22。先讀 [架構](ARCHITECTURE.md) 與 [規則](AGENTS.md)。

這是獨立可建置的起始範例，不是正式來源插件；沒有帳密、cache schema、WebView2 或網路操作。

2026-09-26：依使用者決定將範例套件基線對齊 SDK 1.3.0，保留 AssemblyVersion 1.0.0.0；增加 capability 與 assembly identity 相容性測試。這是新增插件的當前起點，仍不預設實作 tag 字典。

同日以主程式的明確 local-feed `NuGet.config` 及獨立 packages/artifacts 路徑執行 `eng/Verify-Plugin.ps1`：**8/8 離線測試通過**，套件 metadata、架構邊界、正式輸出及文件連結檢查通過。遠端 GitHub Packages／CI 還未驗證。

複製到新 repo 後執行 `pwsh -NoProfile -File eng/Verify-Plugin.ps1`。GitHub Packages 的讀取授權由使用者／CI 環境提供，不把憑證存進設定。

本地尚未發布時，可向 Verify-Plugin 傳入主程式 eng/PackageValidation/NuGet.config 的絕對路徑，以及工作區內獨立 ArtifactsPath/PackagesPath。範例程式與 tests 本身不依賴 sibling source。

修改後在此記錄：目的、改動層、公開／資料相容性、實際命令與結果、人工驗證、剩餘問題。不要代替其他 agent 勾選「已讀」。最終範例驗證結果見主程式整體交接。

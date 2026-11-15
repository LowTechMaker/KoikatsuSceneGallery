# 交接驗證

在專案根目錄以 PowerShell 7 執行：

```
rtk proxy pwsh -NoProfile -File eng/Verify-Handoffs.ps1
```

成功時輸出 `PASS: <N> handoffs; current markers pending=<未讀>, read=<已讀>; <M> local links resolved.`，失敗時丟出例外並說明是哪一份。

## 檢查項目

1. 每一份 `docs/handoffs/phase-*.md` 在 `docs/handoffs/INDEX.md` 都有直接連結 `](phase-NN.md)`。
2. 每份都有 `- 交接版本：vN` 行。
3. 目前版本恰好有一條 Claude 已讀欄。
4. 已勾選的欄位有含時區的 ISO 8601 時間與閱讀者／任務。
5. `CLAUDE.md`、`docs/handoffs/*.md`、本 skill 的 Markdown 中所有本機連結都指得到檔案。

程式碼區塊（```）內的內容不檢查，所以範例不會被誤判。

## 不檢查（也無法證明）的事

- 是否真的讀過。
- 驗收是否通過、驗證命令是否真的跑過。
- 交接內容是否與工作樹一致。

這些靠 SKILL.md 的安全邊界與失敗政策約束，不可因為腳本通過就省略。

## 腳本失敗時

修正交接或索引，不修改腳本放寬檢查。若檢查規則本身需要改變，另寫一份交接說明理由。

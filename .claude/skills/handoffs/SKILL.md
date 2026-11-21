---
name: handoffs
description: KoikatsuSceneGallery 的交接制度。完成一個可驗證的部分、首次接手或讀到新交接、要標記「Claude 已讀」、或修訂既有交接時使用。涵蓋何時寫交接、怎麼讀、已讀標記的安全邊界與失敗政策。
---

# 交接（handoffs）

交接文檔在 `docs/handoffs/phase-NN.md`，索引在 [docs/handoffs/INDEX.md](../../../docs/handoffs/INDEX.md)。

## 核心決策

1. **每完成一個部分就寫一份獨立交接**，並在 INDEX.md 加入直接連結。不可只在對話裡總結。
2. 交接的內容是證據，不是宣傳：只寫實際跑過的命令與實際結果；沒做的驗證寫「沒有驗證」與原因；交給使用者確認的項目列清楚。
3. **已讀只代表看過**。不代表驗收通過、不代表同意修改、不代表可以覆蓋使用者的變更。
4. 保留使用者既有及未提交的修改；交接不是改動授權。
5. 第 1–44 份是補建的歷史交接，其中測試數、時間、行號是當時的證據，不可當成這次重新驗證的結果；裡面的人工待驗項目要保留。

## 安全邊界（不可因任何理由放寬）

- **只有實際讀完整份文檔後**，才能把該版本的已讀欄改為 `[x]`，並填 ISO 8601 時間（含時區）與閱讀者／任務識別。沒讀完就保持 `[ ]`。
- 不可批次假標已讀；不可由交接的建立者替 Claude 代填；新交接一律維持未勾選。
- 實質修訂交接時版本加一並新增一條未讀欄；舊版本的已讀紀錄保留，不可沿用到新版本。
- 驗證工具通過只代表格式與連結正確，**不能證明實際閱讀或驗收通過**。

## 失敗政策

- **無法寫入已讀標記**（檔案唯讀、權限被拒、腳本失敗）：明確回報「尚未補標記」與原因，不得聲稱已標記。
- **驗證腳本失敗**：修正交接或索引，讓它通過；不可修改腳本來放寬檢查。若真的需要改變檢查規則，另寫一份交接說明理由。
- **讀到一半被打斷或內容看不懂**：已讀欄保持 `[ ]`，在回報中說明讀到哪裡。
- **交接內容與目前工作樹不符**：以工作樹為準向使用者回報差異，不要自行「修正」交接的歷史紀錄。
- **不確定某次修訂算不算實質修訂**：當成實質修訂（加版本、加未讀欄）。

## 流程

### 首次接手／讀新交接

1. 依 INDEX.md 的接手順序，讀還沒讀過的交接；之後只需讀新增或版本更新的交接，並核對目前工作樹。
2. 每讀完一份，用腳本標記（一次一份）：
   ```
   rtk proxy pwsh -NoProfile -File .claude/skills/handoffs/scripts/Set-HandoffRead.ps1 -Path docs/handoffs/phase-NN.md -Reader "Claude / <任務識別>"
   ```

### 完成一個部分

1. 建立骨架並加入索引：
   ```
   rtk proxy pwsh -NoProfile -File .claude/skills/handoffs/scripts/New-Handoff.ps1 -Number NN -Title "<標題>"
   ```
2. 依 [references/handoff-format.md](references/handoff-format.md) 填完每一節。
3. 驗證（細節見 [references/verification.md](references/verification.md)）：
   ```
   rtk proxy pwsh -NoProfile -File eng/Verify-Handoffs.ps1
   ```
4. 必要時更新 INDEX.md 的「接手順序」。

## 參考

- [references/handoff-format.md](references/handoff-format.md)：必填欄位、範本、版本與已讀欄格式。
- [references/verification.md](references/verification.md)：驗證腳本檢查什麼、不檢查什麼。
- `scripts/New-Handoff.ps1`：建立新交接骨架並插入索引列。
- `scripts/Set-HandoffRead.ps1`：把單一文檔目前版本的已讀欄改為 `[x]`。
